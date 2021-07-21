using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace CriPakTools {
    class Program {
        static void Main(string[] args) {
            Console.WriteLine("CriPakTools.NetCore p.003\n");
            Console.WriteLine(
                "Based off Falo's code relased on Xentax forums (see readme.txt), modded by Nanashi3 from FuwaNovels.\nInsertion code by EsperKnight\n\n");

            if (args.Length == 0) {
                Console.WriteLine("CriPakTool Usage:\n");
                Console.WriteLine("CriPakTool.exe IN_FILE - Displays all contained chunks.\n");
                Console.WriteLine("CriPakTool.exe IN_FILE EXTRACT_ME - Extracts a file.\n");
                Console.WriteLine("CriPakTool.exe IN_FILE ALL - Extracts all files.\n");
                Console.WriteLine(
                    "CriPakTool.exe IN_FILE REPLACE_ME REPLACE_WITH [OUT_FILE] - Replaces REPLACE_ME with REPLACE_WITH.  Optional output it as a new CPK file otherwise it's replaced.\n");
                return;
            }
            
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // for Shift-JIS: https://stackoverflow.com/a/40549315

            string cpk_name = args[0];

            CPK cpk = new CPK(new Tools());
            cpk.ReadCPK(cpk_name);

            BinaryReader oldFile = new BinaryReader(File.OpenRead(cpk_name));

            if (args.Length == 1) {
                List<FileEntry> entries = cpk.FileTable.OrderBy(x => x.FileOffset).ToList();
                for (int i = 0; i < entries.Count; i++) {
                    Console.WriteLine(((entries[i].DirName != null) ? entries[i].DirName + "/" : "") +
                                      entries[i].FileName);
                }
            }
            else if (args.Length == 2) {
                string extractMe = args[1];

                List<FileEntry> entries = null;

                if (extractMe.ToUpper() == "ALL")
                    entries = cpk.FileTable.Where(x => x.FileType == "FILE").ToList();
                else if (extractMe.ToUpper().StartsWith("MATCH:")) {
                    Console.WriteLine("MATCH MODE");
                    var matchPattern = extractMe.Substring(6);
                    entries = cpk.FileTable
                        .Where(x => x.FileType == "FILE")
                        .Where(x => Wildcard.match(matchPattern, (string) x.FileName))
                        .ToList();
                }
                else
                    entries = cpk.FileTable.Where(x =>
                        ((x.DirName != null) ? x.DirName + "/" : "") + x.FileName.ToString().ToLower() ==
                        extractMe.ToLower()).ToList();

                if (entries.Count == 0) {
                    Console.WriteLine("Cannot find " + extractMe + ".");
                }

                for (int i = 0; i < entries.Count; i++) {
                    if (!String.IsNullOrEmpty((string) entries[i].DirName)) {
                        Directory.CreateDirectory(entries[i].DirName.ToString());
                    }

                    var entryInternalFn = ((entries[i].DirName != null) ? entries[i].DirName + "/" : "") +
                        entries[i].FileName.ToString(); // internal file name

                    try {
                        oldFile.BaseStream.Seek((long) entries[i].FileOffset, SeekOrigin.Begin);
                        string isComp = Encoding.ASCII.GetString(oldFile.ReadBytes(8));
                        oldFile.BaseStream.Seek((long) entries[i].FileOffset, SeekOrigin.Begin);

                        byte[] chunk = oldFile.ReadBytes(Int32.Parse(entries[i].FileSize.ToString()));
                        if (isComp == "CRILAYLA") {
                            int size = Int32.Parse((entries[i].ExtractSize ?? entries[i].FileSize).ToString());
                            chunk = cpk.DecompressCRILAYLA(chunk, size);
                        }

                        Console.WriteLine("Extracting: " + entryInternalFn);
                        File.WriteAllBytes(
                            ((entries[i].DirName != null) ? entries[i].DirName + "/" : "") + entries[i].FileName.ToString(),
                            chunk);
                    }
                    catch (Exception ex) {
                        Console.WriteLine("Failed to extract: " + entryInternalFn + ", exception: " + ex.ToString());
                    }
                }
            }
            else {
                if (args.Length < 3) {
                    Console.WriteLine("Usage for insertion CriPakTools IN_CPK REPLACE_THIS REPLACE_WITH [OUT_CPK]");
                    return;
                }

                string ins_name = args[1];
                string replace_with = args[2];

                FileInfo fi = new FileInfo(cpk_name);

                string outputName = fi.FullName + ".tmp";
                if (args.Length >= 4) {
                    outputName = fi.DirectoryName + "\\" + args[3];
                }

                BinaryWriter newCPK = new BinaryWriter(File.OpenWrite(outputName));

                List<FileEntry> entries = cpk.FileTable.OrderBy(x => x.FileOffset).ToList();

                for (int i = 0; i < entries.Count; i++) {
                    if (entries[i].FileType != "CONTENT") {
                        if (entries[i].FileType == "FILE") {
                            // I'm too lazy to figure out how to update the ContextOffset position so this works :)
                            if ((ulong) newCPK.BaseStream.Position < cpk.ContentOffset) {
                                ulong padLength = cpk.ContentOffset - (ulong) newCPK.BaseStream.Position;
                                for (ulong z = 0; z < padLength; z++) {
                                    newCPK.Write((byte) 0);
                                }
                            }
                        }


                        if (entries[i].FileName.ToString() != ins_name) {
                            oldFile.BaseStream.Seek((long) entries[i].FileOffset, SeekOrigin.Begin);

                            entries[i].FileOffset = (ulong) newCPK.BaseStream.Position;
                            cpk.UpdateFileEntry(entries[i]);

                            byte[] chunk = oldFile.ReadBytes(Int32.Parse(entries[i].FileSize.ToString()));
                            newCPK.Write(chunk);
                        }
                        else {
                            byte[] newbie = File.ReadAllBytes(replace_with);
                            entries[i].FileOffset = (ulong) newCPK.BaseStream.Position;
                            entries[i].FileSize = Convert.ChangeType(newbie.Length, entries[i].FileSizeType);
                            entries[i].ExtractSize = Convert.ChangeType(newbie.Length, entries[i].FileSizeType);
                            cpk.UpdateFileEntry(entries[i]);
                            newCPK.Write(newbie);
                        }

                        if ((newCPK.BaseStream.Position % 0x800) > 0) {
                            long cur_pos = newCPK.BaseStream.Position;
                            for (int j = 0; j < (0x800 - (cur_pos % 0x800)); j++) {
                                newCPK.Write((byte) 0);
                            }
                        }
                    }
                    else {
                        // Content is special.... just update the position
                        cpk.UpdateFileEntry(entries[i]);
                    }
                }

                cpk.WriteCPK(newCPK);
                cpk.WriteITOC(newCPK);
                cpk.WriteTOC(newCPK);
                cpk.WriteETOC(newCPK);
                cpk.WriteGTOC(newCPK);

                newCPK.Close();
                oldFile.Close();

                if (args.Length < 4) {
                    File.Delete(cpk_name);
                    File.Move(outputName, cpk_name);
                    File.Delete(outputName);
                }
            }
        }
    }

    public static class Wildcard {
        public static bool match(string pattern, string str) {
            return System.Text.RegularExpressions.Regex.IsMatch(str,
                "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") +
                "$");
        }

        public static bool isRaw(string pattern) {
            return !pattern.Contains('?') && !pattern.Contains('*');
        }
    }
}