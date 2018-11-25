﻿#pragma warning disable 0108
using com.clusterrr.Famicom;
using com.clusterrr.hakchi_gui.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml.XPath;

namespace com.clusterrr.hakchi_gui
{
    public class NesGame : NesApplication, ICloverAutofill, ISupportsGameGenie
    {
        public const char Prefix = 'H';
        public static bool? IgnoreMapper;
        const string DefaultArgs = "--guest-overscan-dimensions 0,0,9,3 --initial-fadein-durations 10,2 --volume 75 --enable-armet";
        private static Dictionary<uint, CachedGameInfo> gameInfoCache = null;

        private static byte[] supportedMappers = new byte[] { 0, 1, 2, 3, 4, 5, 7, 9, 10, 86, 87, 184 };

        public NesGame(string path, AppMetadata metadata = null, bool ignoreEmptyConfig = false)
            : base(path, metadata, ignoreEmptyConfig)
        {
        }

        public static bool Patch(string inputFileName, ref byte[] rawRomData, ref char prefix, ref string application, ref string outputFileName, ref string args, ref Image cover, ref byte saveCount, ref uint crc32)
        {
            // Try to patch before mapper check, maybe it will patch mapper
            var patched = FindPatch(ref rawRomData, inputFileName, crc32);
            NesFile nesFile;
            try
            {
                nesFile = new NesFile(rawRomData);
            }
            catch
            {
                return true;
            }
            crc32 = nesFile.CRC32;

            // Also search for patch using internal CRC32
            if (!patched)
            {
                if (FindPatch(ref rawRomData, inputFileName, crc32))
                    nesFile = new NesFile(rawRomData);
            }
            nesFile.CorrectRom();

            application = "/bin/clover-kachikachi-wr";
            args = DefaultArgs;

            if (!supportedMappers.Contains(nesFile.Mapper) && (IgnoreMapper != true))
            {
                if (IgnoreMapper != false)
                {
                    var result = Tasks.MessageForm.Show(ParentForm, Resources.AreYouSure,
                        string.Format(Resources.MapperNotSupported, Path.GetFileName(inputFileName), nesFile.Mapper),
                        Resources.sign_warning,
                        new Tasks.MessageForm.Button[] { Tasks.MessageForm.Button.YesToAll, Tasks.MessageForm.Button.Yes, Tasks.MessageForm.Button.No },
                        Tasks.MessageForm.DefaultButton.Button2);
                    if (result == Tasks.MessageForm.Button.YesToAll)
                        IgnoreMapper = true;
                    if (result == Tasks.MessageForm.Button.No)
                        return false;
                }
                else return false;
            }

            if ((nesFile.Mirroring == NesFile.MirroringType.FourScreenVram) && (IgnoreMapper != true))
            {
                var result = Tasks.MessageForm.Show(ParentForm, Resources.AreYouSure,
                    string.Format(Resources.FourScreenNotSupported, Path.GetFileName(inputFileName)),
                    Resources.sign_warning,
                    new Tasks.MessageForm.Button[] { Tasks.MessageForm.Button.YesToAll, Tasks.MessageForm.Button.Yes, Tasks.MessageForm.Button.No },
                    Tasks.MessageForm.DefaultButton.Button2);
                if (result == Tasks.MessageForm.Button.YesToAll)
                    IgnoreMapper = true;
                if (result == Tasks.MessageForm.Button.No)
                    return false;
            }

            // TODO: Make trainer check. I think that the NES Mini doesn't support it.
            rawRomData = nesFile.GetRaw();
            if (inputFileName.Contains("(J)")) cover = Resources.blank_jp;

            if (nesFile.Battery)
                saveCount = 3;

            return true;
        }

        public bool TryAutofill(uint crc32)
        {
            CachedGameInfo gameinfo;
            if (gameInfoCache != null && gameInfoCache.TryGetValue(crc32, out gameinfo))
            {
                Name = gameinfo.Name;
                Name = Name.Replace("_", " ").Replace("  ", " ").Trim();
                desktop.Players = gameinfo.Players;
                if (desktop.Players > 1) desktop.Simultaneous = true; // actually unknown...
                string releaseDate = gameinfo.ReleaseDate;
                    if (releaseDate.Length == 4) releaseDate += "-01";
                    if (releaseDate.Length == 7) releaseDate += "-01";
                desktop.ReleaseDate = releaseDate;
                desktop.Publisher = gameinfo.Publisher.ToUpper();
                return true;
            }
            return false;
        }

        private struct CachedGameInfo
        {
            public string Name;
            public byte Players;
            public string ReleaseDate;
            public string Publisher;
            public string Region;
        }

        public static void LoadCache()
        {
            try
            {
                var xmlDataBasePath = Path.Combine(System.IO.Path.Combine(Program.BaseDirectoryInternal, "data"), "nescarts.xml");
                Trace.WriteLine("Loading " + xmlDataBasePath);

                if (File.Exists(xmlDataBasePath))
                {
                    var xpath = new XPathDocument(xmlDataBasePath);
                    var navigator = xpath.CreateNavigator();
                    var iterator = navigator.Select("/database/game");
                    gameInfoCache = new Dictionary<uint, CachedGameInfo>();
                    while (iterator.MoveNext())
                    {
                        XPathNavigator game = iterator.Current;
                        var cartridges = game.Select("cartridge");
                        while (cartridges.MoveNext())
                        {
                            var cartridge = cartridges.Current;
                            try
                            {
                                var crc = Convert.ToUInt32(cartridge.GetAttribute("crc", ""), 16);
                                gameInfoCache[crc] = new CachedGameInfo
                                {
                                    Name = game.GetAttribute("name", ""),
                                    Players = (byte)((game.GetAttribute("players", "") != "1") ? 2 : 1),
                                    ReleaseDate = game.GetAttribute("date", ""),
                                    Publisher = game.GetAttribute("publisher", ""),
                                    Region = game.GetAttribute("region", "")
                                };
                            }
                            catch { }
                        };
                    }
                }
                Trace.WriteLine(string.Format("NES XML loading done, {0} roms total", gameInfoCache.Count));
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Probable data/nescarts.xml file corruption: " + ex.Message + ex.StackTrace);
            }
        }

        public bool ApplyGameGenie(out byte[] gameFileData)
        {
            gameFileData = null;
            if (!string.IsNullOrEmpty(GameGenie))
            {
                var codes = GameGenie.Split(new char[] { ',', '\t', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
                string gameFilePath = GameFilePath;
                if (gameFilePath != null)
                {
                    byte[] data = GameFileData;
                    if (data != null)
                    {
                        var nesFile = new NesFile(data);
                        foreach (var code in codes)
                        {
                            nesFile.PRG = GameGeniePatcherNes.Patch(nesFile.PRG, code.Trim());
                        }
                        gameFileData = nesFile.GetRaw();
                        return true;
                    }
                }
            }
            return false;
        }

        public void ApplyGameGenie()
        {
            if (!string.IsNullOrEmpty(GameGenie))
            {
                bool wasCompressed = DecompressPossible().Length > 0;
                if (wasCompressed)
                    Decompress();

                var codes = GameGenie.Split(new char[] { ',', '\t', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
                var nesFiles = Directory.GetFiles(this.basePath, "*.nes", SearchOption.TopDirectoryOnly);
                foreach (var f in nesFiles)
                {
                    var nesFile = new NesFile(f);
                    foreach (var code in codes)
                    {
                        nesFile.PRG = GameGeniePatcherNes.Patch(nesFile.PRG, code.Trim());
                    }
                    nesFile.Save(f);
                }

                if(wasCompressed)
                    Compress();
            }
        }
    }
}

