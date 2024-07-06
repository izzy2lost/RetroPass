using System.Xml.Serialization;
using Windows.Storage;

namespace RetroPass
{
    public class Platform
    {
        public enum EEmulatorType
        {
            retroarch,
            rgx,
            xbsx2,
            dolphin,
            xenia,
            xeniacanary,
            ppsspp,
            duckstation,
            citrara,
        }

        public string Name { get; set; }
        public string SourceName { get; set; }
        public EEmulatorType EmulatorType { get; set; }
        public string BoxFrontPath { get; set; }
        public string ScreenshotGameTitlePath { get; set; }
        public string ScreenshotGameplayPath { get; set; }
        public string ScreenshotGameSelectPath { get; set; }
        public string VideoPath { get; set; }

        [XmlIgnoreAttribute]
        public StorageFolder BoxFrontFolder { get; set; }

        public void SetEmulatorType(string emulatorPath)
        {
            if (string.IsNullOrEmpty(emulatorPath) == false &&
                    (emulatorPath.Contains("pcsx2", System.StringComparison.CurrentCultureIgnoreCase) ||
                    emulatorPath.Contains("xbsx2", System.StringComparison.CurrentCultureIgnoreCase))
                )
            {
                EmulatorType = EEmulatorType.xbsx2;
            }
            else if (string.IsNullOrEmpty(emulatorPath) == false && emulatorPath.Contains("retrix", System.StringComparison.CurrentCultureIgnoreCase))
            {
                EmulatorType = EEmulatorType.rgx;
            }
            else if (string.IsNullOrEmpty(emulatorPath) == false && emulatorPath.Contains("dolphin", System.StringComparison.CurrentCultureIgnoreCase))
            {
                EmulatorType = EEmulatorType.dolphin;
            }
            else if (string.IsNullOrEmpty(emulatorPath) == false &&
                (emulatorPath.Contains("xenia-canary", System.StringComparison.CurrentCultureIgnoreCase) ||
                emulatorPath.Contains("xeniacanary", System.StringComparison.CurrentCultureIgnoreCase))
                )
            {
                EmulatorType = EEmulatorType.xeniacanary;
            }
            else if (string.IsNullOrEmpty(emulatorPath) == false && emulatorPath.Contains("xenia", System.StringComparison.CurrentCultureIgnoreCase))
            {
                EmulatorType = EEmulatorType.xenia;
            }
            else if (string.IsNullOrEmpty(emulatorPath) == false && emulatorPath.Contains("ppsspp", System.StringComparison.CurrentCultureIgnoreCase))
            {
                EmulatorType = EEmulatorType.ppsspp;
            }
            else if (string.IsNullOrEmpty(emulatorPath) == false && emulatorPath.Contains("duckstation", System.StringComparison.CurrentCultureIgnoreCase))
            {
                EmulatorType = EEmulatorType.duckstation;
            }
            else if (string.IsNullOrEmpty(emulatorPath) == false && emulatorPath.Contains("citrara", System.StringComparison.CurrentCultureIgnoreCase))
            {
                EmulatorType = EEmulatorType.citrara;
            }
            else
            {
                //let it just be default retroarch
                EmulatorType = EEmulatorType.retroarch;
            }
        }

        public Platform Copy()
        {
            return (Platform)this.MemberwiseClone();
        }
    }
}
