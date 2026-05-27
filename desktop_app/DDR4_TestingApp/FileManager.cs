using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;

namespace DDR4_TestingApp
{
    internal static class FileManager
    {

        //Capture file extension
        private static readonly String CaptureExtension = ".cap";

        //Path to workspace
        static public String WorkspacePath = "C:\\";
        static private bool isWorkspaceLoaded = false;

        //Check to ensure the workspace is valid
        public static bool IsValidWorkspace(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (!Directory.Exists(path)) return false;
            if (Path.GetInvalidPathChars().Any(path.Contains)) return false;

            // Check we can actually read it (not locked/permission denied)
            try
            {
                Directory.GetFiles(path);
                return true;
            }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return false; }
        }

        //Load a provided path as a workspace
        public static bool LoadWorkspace(string path)
        {
            //Validate the path provided
            if (!IsValidWorkspace(path))
                return false;

            //Attempt to load all captures in the folder
            foreach(String p in GetCaptureFiles(path))
            {
                //Attempt to load the capture file
                Capture c = Load(p);

                //Store the capture file in the data logger for use
                DataLogger.captures.Add(c);
            }

            //Mark workspace as loaded
            isWorkspaceLoaded = true;

            //Return success
            return true;       
        }

        //Generate a file name for a capture 
        public static String GenerateName(Capture c)
        {
            return c.name + CaptureExtension;
        }

        //Get all captures in the workspace
        public static List<string> GetCaptureFiles(string path)
        {
            if (!IsValidWorkspace(path))
                return new List<string>();

            return Directory.GetFiles(path, $"*{CaptureExtension}")
                            .ToList();
        }

        //Write a capture to file
        public static void Write(string path, Capture c)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                using (BinaryWriter writer = new BinaryWriter(File.Open(path, FileMode.Create)))
                {
                    // Write capture header
                    writer.Write(c.name);               // String
                    writer.Write(c.time);               // UInt64

                    // Write number of blocks
                    writer.Write(c.blocks.Count);       // Int32

                    // Write each block
                    foreach (MemoryBlock block in c.blocks)
                    {
                        writer.Write(block.address);    // UInt32
                        writer.Write(block.size);       // UInt32
                        writer.Write(block.data.Length);// Int32  (byte count)
                        writer.Write(block.data);       // Byte[]
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Write failed: {ex.GetType().Name} — {ex.Message}");
            }
        }

        //Read a capture from file
        public static Capture Load(string path)
        {
            Capture c = new Capture();

            try
            {
                using (BinaryReader reader = new BinaryReader(File.Open(path, FileMode.Open)))
                {
                    // Read capture header
                    c.name = reader.ReadString();      // String
                    c.time = reader.ReadUInt64();      // UInt64

                    // Read blocks
                    int blockCount = reader.ReadInt32();
                    c.blocks = new List<MemoryBlock>(blockCount);

                    for (int i = 0; i < blockCount; i++)
                    {
                        MemoryBlock block = new MemoryBlock();
                        block.address = reader.ReadUInt32();   // UInt32
                        block.size = reader.ReadUInt32();   // UInt32
                        int dataLength = reader.ReadInt32();    // Int32
                        block.data = reader.ReadBytes(dataLength); // Byte[]
                        c.blocks.Add(block);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Read failed: {ex.GetType().Name} — {ex.Message}");
            }

            return c;
        }





    }
}
