﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Windows.Storage;
using Windows.Storage.Search;
using Windows.UI.Composition;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;

namespace RetroPass
{
	public class DataSourceManager
	{
		readonly string ActiveDataSourcesKey = "ActiveDataSourcesKey";

		public ObservableCollection<DataSource> dataSources {get; private set; } = new ObservableCollection<DataSource>();
		public Action DataSourcesChanged;
		public Action ActiveDataSourcesChanged;

		public enum ValidationResult
		{
			DUPLICATE_PATH,
			UNKNOWN_DATA_SOURCE_TYPE,
			DUPLICATE_NAME,
		}

		public DataSourceManager()
		{
			//delete all settings
			//_ = ApplicationData.Current.ClearAsync();
		}

		private void SaveActiveDataSources()
		{
			List<RetroPassConfig> list = dataSources.
				Where(t => t.status == DataSource.Status.Active || t.status == DataSource.Status.Unavailable).
				Select(r => r.retroPassConfig).ToList();
			XmlSerializer serializer = new XmlSerializer(typeof(List<RetroPassConfig>));
			using (StringWriter writer = new StringWriter())
			{
				serializer.Serialize(writer, list);
				string stringListAsXml = writer.ToString();
				ApplicationData.Current.LocalSettings.Values[ActiveDataSourcesKey] = stringListAsXml;
			}
		}

		private List<RetroPassConfig> LoadActiveDataSources()
		{
			List<RetroPassConfig> list = new List<RetroPassConfig>();

			if (ApplicationData.Current.LocalSettings.Values[ActiveDataSourcesKey] != null)
			{
				string listXml = (string)ApplicationData.Current.LocalSettings.Values[ActiveDataSourcesKey];

				// Deserialize the JSON string to get back the original list of strings
				XmlSerializer serializer = new XmlSerializer(typeof(List<RetroPassConfig>));
				using (StringReader reader = new StringReader(listXml))
				{
					list = (List<RetroPassConfig>)serializer.Deserialize(reader);
				}
			}

			return list;
		}

		public async Task<(DataSource dataSource, List<ValidationResult> validationResult)> ValidateDataSource(string path)
		{
			List<ValidationResult> validationResult = new List<ValidationResult>();

			string pathRoot = Path.GetPathRoot(path);
			string pathRelative = Path.GetRelativePath(pathRoot, path);
			string pathConfig = Path.Combine(pathRoot, "RetroPass.xml");

			//check if file path is already in data sources
			if (dataSources.FirstOrDefault(t => string.IsNullOrEmpty(t.rootFolder) == false && 
			StorageUtils.NormalizePath(t.rootFolder) == StorageUtils.NormalizePath(path)) != null)
			{
				validationResult.Add(ValidationResult.DUPLICATE_PATH);
			}

			//check if there is any valid data source on the path
			string pathLb = Path.GetFullPath(Path.Combine(path, "Data", "Platforms.xml"));
			
			StorageFile lbFile = await StorageUtils.GetFileFromPathAsync(pathLb);
			DataSource dataSource = null;
			
			//this is launchbox data source
			if (lbFile != null)
			{
				RetroPassConfig retroPassConfig = new RetroPassConfig();
				retroPassConfig.type = RetroPassConfig.DataSourceType.LaunchBox;
				retroPassConfig.relativePath = Path.GetRelativePath(pathRoot, path);
				retroPassConfig.name = Path.GetFileName(path);
				dataSource = new DataSourceLaunchBox(path, retroPassConfig);
			}
			else
			{
				StorageFolder dataFolder = await StorageUtils.GetFolderFromPathAsync(path);

				if(dataFolder != null)
				{
					//find es_systems.cfg file
					QueryOptions options = new QueryOptions();
					options.ApplicationSearchFilter = "System.FileName:~\"" + "es_systems.cfg" + "\"";
					options.FolderDepth = FolderDepth.Deep;
					StorageFileQueryResult queryResult = dataFolder.CreateFileQueryWithOptions(options);
					IReadOnlyList<StorageFile> sortedFiles = await queryResult.GetFilesAsync();
					if (sortedFiles.Count > 0)
					{
						RetroPassConfig retroPassConfig = new RetroPassConfig();
						retroPassConfig.type = RetroPassConfig.DataSourceType.EmulationStation;
						retroPassConfig.relativePath = Path.GetRelativePath(pathRoot, path);
						retroPassConfig.name = Path.GetFileName(path);
						dataSource = new DataSourceEmulationStation(path, retroPassConfig);
					}
				}				
			}

			if (dataSource == null)
			{
				validationResult.Add(ValidationResult.UNKNOWN_DATA_SOURCE_TYPE);
			}
			//check if there is already a data source with exact name
			else if (dataSources.FirstOrDefault(t => t.retroPassConfig.name == dataSource.retroPassConfig.name) != null)
			{
				validationResult.Add(ValidationResult.DUPLICATE_NAME);
			}

			return (dataSource, validationResult);
		}

		public async Task AddDataSource(DataSource dataSource)
		{
			string pathRoot = Path.GetPathRoot(dataSource.rootFolder);

			if (dataSource != null)
			{
				StorageFolder volume = await StorageFolder.GetFolderFromPathAsync(pathRoot);

				if (volume != null)
				{
					var xmlConfigFile = await volume.TryGetItemAsync("RetroPass.xml") as StorageFile;
					List<DataSource> dataSources = new List<DataSource>();
					
					if (xmlConfigFile != null)
					{
						dataSources = await GetDataSourcesFromConfigurationFile(xmlConfigFile);						
					}
					//there is no xml the first time source is added for the volume
					else
					{
						xmlConfigFile = await volume.CreateFileAsync("RetroPass.xml");						
					}

					if (xmlConfigFile != null)
					{
						dataSources.Add(dataSource);
						await SaveDataSourcesToConfigurationFile(dataSources, xmlConfigFile);
					}
				}
			}
		}

		private async Task SaveDataSourcesToConfigurationFile(List<DataSource> dataSources, StorageFile xmlConfigFile)
		{
			List<RetroPassConfig> list = dataSources.Select(r => r.retroPassConfig).ToList();
			RetroPassConfig_V1_6 config = new RetroPassConfig_V1_6();
			
			config.dataSources = list;
			XmlSerializer serializer = new XmlSerializer(typeof(RetroPassConfig_V1_6));
			XmlSerializerNamespaces namespaces = new XmlSerializerNamespaces();
			namespaces.Add("", "");
			using (StringWriter writer = new StringWriter())
			{
				serializer.Serialize(writer, config, namespaces);
				string stringListAsXml = writer.ToString();

				using (TextWriter fileWriter = File.CreateText(xmlConfigFile.Path))
				{
					await fileWriter.WriteAsync(stringListAsXml);
				}
			}
		}

		public async Task ScanDataSources()
		{
			Trace.TraceInformation("DataSourceManager: ScanDataSource");
			// Get the logical root folder for all external storage devices.
			IReadOnlyList<StorageFolder> removableFolders = await KnownFolders.RemovableDevices.GetFoldersAsync();

			List<DataSource> dataSourcesFromConfig = new List<DataSource>();
			//read from settings all data sources that are saved as active
			List<RetroPassConfig> activeDataSourceConfigsFromSettings = LoadActiveDataSources();

			foreach (var folder in removableFolders)
			{
				if (folder != null)
				{
					//check if the location exists
					var item = await folder.TryGetItemAsync("RetroPass.xml");

					if (item != null)
					{
						var dsConfig = await GetDataSourcesFromConfigurationFile(item as StorageFile);
						dataSourcesFromConfig.AddRange(dsConfig);						
					}
				}
			}

			List<string> allDataSourceNames = new List<string>();
			allDataSourceNames.AddRange(dataSources.Select(t => t.retroPassConfig.name).ToList());
			allDataSourceNames.AddRange(dataSourcesFromConfig.Select(t => t.retroPassConfig.name).ToList());
			allDataSourceNames.AddRange(activeDataSourceConfigsFromSettings.Select(t => t.name).ToList());
			allDataSourceNames = allDataSourceNames.Distinct().ToList();

			List<DataSource> dataSourcesChanged = new List<DataSource>();
			List<DataSource> activeDataSourcesChanged = new List<DataSource>();

			foreach (var dataSourceName in allDataSourceNames)
			{
				DataSource dataSourceInCurrent = dataSources.FirstOrDefault(t => t.retroPassConfig.name == dataSourceName);
				DataSource dataSourceInConfig = dataSourcesFromConfig.FirstOrDefault(t => t.retroPassConfig.name == dataSourceName);
				RetroPassConfig dataSourceInActive = activeDataSourceConfigsFromSettings.FirstOrDefault(t => t.name == dataSourceName);
				
				//data source is already loaded, available from usb and set as active
				//true true true
				if (dataSourceInCurrent != null && dataSourceInConfig != null && dataSourceInActive != null)
				{
					if(dataSourceInCurrent.status != DataSource.Status.Active)
					{
						dataSourceInCurrent.status = DataSource.Status.Active;
						dataSourceInCurrent.rootFolder = dataSourceInConfig.rootFolder;
						dataSourcesChanged.Add(dataSourceInCurrent);
						activeDataSourcesChanged.Add(dataSourceInCurrent);
					}					
				}
				//data source is already loaded, available from usb and not set as active from previous sessions
				//true true false
				else if (dataSourceInCurrent != null && dataSourceInConfig != null && dataSourceInActive == null)
				{
					if (dataSourceInCurrent.status != DataSource.Status.Inactive)
					{
						if(dataSourceInCurrent.status == DataSource.Status.Active)
						{
							activeDataSourcesChanged.Add(dataSourceInCurrent);
						}

						dataSourceInCurrent.status = DataSource.Status.Inactive;
						dataSourcesChanged.Add(dataSourceInCurrent);
					}
				}
				//data source is already loaded, not available from usb and set as active from previous sessions
				//true false true
				else if (dataSourceInCurrent != null && dataSourceInConfig == null && dataSourceInActive != null)
				{
					if (dataSourceInCurrent.status != DataSource.Status.Unavailable)
					{
						if (dataSourceInCurrent.status == DataSource.Status.Active)
						{
							activeDataSourcesChanged.Add(dataSourceInCurrent);
						}

						dataSourceInCurrent.status = DataSource.Status.Unavailable;
						dataSourcesChanged.Add(dataSourceInCurrent);
					}					
				}
				//data source is already loaded, not available from usb and not set as active from previous sessions
				//true false false
				else if (dataSourceInCurrent != null && dataSourceInConfig == null && dataSourceInActive == null)
				{
					if (dataSourceInCurrent.status == DataSource.Status.Active)
					{
						activeDataSourcesChanged.Add(dataSourceInCurrent);
					}

					dataSources.Remove(dataSourceInCurrent);
					dataSourcesChanged.Add(null);
				}
				//data source is not already loaded, available from usb and set as active from previous sessions
				//false true true
				else if (dataSourceInCurrent == null && dataSourceInConfig != null && dataSourceInActive != null)
				{
					dataSourceInConfig.status = DataSource.Status.Active;
					dataSources.Add(dataSourceInConfig);
					dataSourcesChanged.Add(dataSourceInConfig);
					activeDataSourcesChanged.Add(dataSourceInConfig);
				}
				//data source is not already loaded, available from usb and not set as active from previous sessions
				//false true false
				else if (dataSourceInCurrent == null && dataSourceInConfig != null && dataSourceInActive == null)
				{
					dataSourceInConfig.status = DataSource.Status.Inactive;
					dataSources.Add(dataSourceInConfig);
					dataSourcesChanged.Add(dataSourceInConfig);
				}
				//data source is not already loaded, not available from usb and set as active from previous sessions
				//false false true
				else if (dataSourceInCurrent == null && dataSourceInConfig == null && dataSourceInActive != null)
				{
					if (dataSourceInActive.type == RetroPassConfig.DataSourceType.LaunchBox)
					{
						DataSourceLaunchBox ds = new DataSourceLaunchBox("", dataSourceInActive);
						ds.status = DataSource.Status.Unavailable;
						dataSources.Add(ds);
						dataSourcesChanged.Add(ds);
					}
					else if (dataSourceInActive.type == RetroPassConfig.DataSourceType.EmulationStation)
					{
						DataSourceEmulationStation ds = new DataSourceEmulationStation("", dataSourceInActive);
						ds.status = DataSource.Status.Unavailable;
						dataSources.Add(ds);
						dataSourcesChanged.Add(ds);
					}
				}
				//data source is not already loaded, not available from usb and set as active from previous sessions
				//false false false
				else if (dataSourceInCurrent == null && dataSourceInConfig == null && dataSourceInActive == null)
				{
					//nothing to do
				}
			}

			if(dataSourcesChanged.Count > 0)
			{
				DataSourcesChanged?.Invoke();
			}

			if (activeDataSourcesChanged.Count > 0)
			{
				ActiveDataSourcesChanged?.Invoke();
			}
		}		

		public void UpdateDataSourceStatus(string name, DataSource.Status status)
		{
			var dataSource = dataSources.FirstOrDefault(t => t.retroPassConfig.name == name);
			if (dataSource != null && dataSource.status != status)
			{
				//active data source is changed if it transitions from inactive/unavailable to active or
				//from active to inactive/unavailable
				if ((status == DataSource.Status.Active || dataSource.status == DataSource.Status.Active))
				{
					ActiveDataSourcesChanged?.Invoke();
				}				
				
				dataSource.status = status;
				SaveActiveDataSources();
				DataSourcesChanged?.Invoke();
			}
		}

		//Gets only those sources that are set by user as active and are connected as external devices
		//It could be that although user activated some sources, they are unplugged, so they need to be filtered out
		public async Task<List<DataSource>> GetPresentActiveDataSources()
		{
			List<DataSource> activeDataSources = new List<DataSource>();

			if (dataSources.Count == 0)
			{
				//find all connected sources
				await ScanDataSources();	
			}

			//filter out all that are not connected
			activeDataSources = dataSources.Where(ds => ds.status == DataSource.Status.Active).ToList();
			return activeDataSources;
		}

		public bool HasDataSources()
		{
			return dataSources.Count > 0;		
		}

		public void DeleteUnavailableDataSources()
		{
			bool found = false;

			for (int i = dataSources.Count - 1; i >= 0; i--)
			{
				if (dataSources[i].status == DataSource.Status.Unavailable)
				{
					found = true;
					dataSources.RemoveAt(i);
				}
			}

			if(found)
			{
				SaveActiveDataSources();
				DataSourcesChanged?.Invoke();
			}
		}

		private async Task<List<DataSource>> GetDataSourcesFromConfigurationFile(StorageFile xmlConfigFile)
		{
			List<DataSource> dataSource = new List<DataSource>();

			if (xmlConfigFile != null)
			{
				Trace.TraceInformation("DataSourceManager: GetDataSourceFromConfigurationFile {0}", xmlConfigFile.Path);
				string xmlConfig = await FileIO.ReadTextAsync(xmlConfigFile);

				using (TextReader reader = new StringReader(xmlConfig))
				{
					List<RetroPassConfig> dataSourceConfigs = new List<RetroPassConfig>();

					string configVersionPattern = @"retropass\s+version\s*=\s*""([\d\.]+)""\s*";
					Regex regex = new Regex(configVersionPattern);
					Match match = regex.Match(xmlConfig);

					if (match.Success && match.Groups[1].Value == "1.6")
					{
						XmlSerializer serializer = new XmlSerializer(typeof(RetroPassConfig_V1_6));
						RetroPassConfig_V1_6 configuration = serializer.Deserialize(reader) as RetroPassConfig_V1_6;
						dataSourceConfigs.AddRange(configuration.dataSources);
					}
					else
					{
						XmlSerializer serializer = new XmlSerializer(typeof(RetroPassConfig));
						RetroPassConfig configuration = serializer.Deserialize(reader) as RetroPassConfig;

						//TODO: try to convert into version 1.6 and save, if it doesn't succeed, load it as old version

						//old config file probably doesn't have name of the data source, so make one
						if (string.IsNullOrEmpty(configuration.name))
						{
							configuration.name = "Removable Storage";
						}

						dataSourceConfigs.Add(configuration);
					}

					foreach (var configuration in dataSourceConfigs)
					{
						string rootFolder = Path.Combine(Path.GetDirectoryName(xmlConfigFile.Path), configuration.relativePath);
						rootFolder = Path.GetFullPath(rootFolder);

						if (configuration.type == RetroPassConfig.DataSourceType.LaunchBox)
						{
							dataSource.Add(new DataSourceLaunchBox(rootFolder, configuration));
						}
						else if (configuration.type == RetroPassConfig.DataSourceType.EmulationStation)
						{
							dataSource.Add(new DataSourceEmulationStation(rootFolder, configuration));
						}

						/*if (dataSource != null)
						{
							string rootFolder = Path.Combine(Path.GetDirectoryName(xmlConfigFile.Path), configuration.relativePath);
							dataSource.rootFolder = Path.GetFullPath(rootFolder);
						}*/
					}
				}
			}

			return dataSource;
		}
	}

}
