using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace TF2_ParticleConverter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Convert_Click(object sender, RoutedEventArgs e)
        {
            RunConversion();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            CheckFile();
        }

        /// <summary>
        /// Run the complete conversion process.
        /// </summary>
        public void RunConversion()
        {
            Console.WriteLine("Team Fortress 2 Particle Converter");
            Console.WriteLine("**********************************");

            Console.WriteLine("Step 1. Converting PNG files to TGA.");
            var index = 0;
            // Loop through all of the PNG images in the 'frames' folder, converting each one to TGA format.
            foreach (var file in Directory.GetFiles("frames\\", "*.png", SearchOption.TopDirectoryOnly))
                // Check that the file name is numeric, as those are the only ones we'll be processing.
                if (file == $"frames\\{index}.png")
                {
                    TGA tga;
                    using (var original = new Bitmap($"frames\\{index}.png"))
                    using (var clone = new Bitmap(original))
                    using (var newbmp = clone.Clone(new Rectangle(0, 0, clone.Width, clone.Height), PixelFormat.Format32bppArgb))
                        tga = (TGA)newbmp;
                    // Save the generated TGA file to the 'frames\converted' folder.
                    tga.Save($"frames\\converted\\frame_{index + 1}.tga");
                    index++;
                }
            // Check that we've processed valid files. If not, then abort the operation.
            if (index == 0)
            {
                Status.Text = "No valid images found. Place your numbered PNG image files (0.png, 1.png etc.) into the 'frames' folder.";
                return;
            }

            Console.WriteLine("Step 2. Locating Team Fortress 2 Directory.");
            // Find the base Steam library path by searching the Registry. Append with the path to the Team Fortress 2 folder.
            var TF2_PATH = (string)Registry.GetValue((Environment.Is64BitProcess) ? @"HKEY_LOCAL_MACHINE\Software\Wow6432Node\Valve\Steam" : @"HKEY_LOCAL_MACHINE\Software\Valve\Steam", "InstallPath", null);
            TF2_PATH += "\\steamapps\\common\\Team Fortress 2\\bin";
            // Check that the Team Fortress 2 directory exists. If not, then abort the operation.
            if (Directory.Exists(TF2_PATH))
            {
                // Create the required folder in the Team Fortress 2 directory.
                if (!Directory.Exists(TF2_PATH + "\\usermod\\materialsrc"))
                    Directory.CreateDirectory(TF2_PATH + "\\usermod\\materialsrc");

                // Copy the gameinfo.txt file to the 'bin' folder if it's not already there.
                File.Copy(TF2_PATH.Replace("bin", "tf") + "\\gameinfo.txt", TF2_PATH + "\\gameinfo.txt", true);

                Console.WriteLine("Step 3. Generating the MKS file.");
                var lines = new List<string>();
                // Start the MKS file with the following header line.
                lines.Add("sequence 0");

                // Add the line 'loop' to the file if that option was checked.
                if (LoopMaterial.IsChecked == true) lines.Add("loop");

                index = 1;
                // Loop through all of the TGA files, recording them in the MKS file then copying them to the TF2 directory.
                foreach (var file in Directory.GetFiles("frames\\converted\\", "*.tga", SearchOption.TopDirectoryOnly))
                    if (file == $"frames\\converted\\frame_{index}.tga")
                    {
                        lines.Add($"frame frame_{index}.tga 1");
                        File.Copy($"frames\\converted\\frame_{index}.tga", TF2_PATH + $"\\frame_{index}.tga", true);
                        index++;
                    }
                WriteFile(lines, $"{TF2_PATH}\\mks_export.mks");

                Console.WriteLine("Step 4. Generating the TGA sheet file.");
                // Call the process to generate SHT and TGA files from the MKS
                using (var process = new Process())
                {
                    process.StartInfo.FileName = "mksheet.exe";
                    process.StartInfo.WorkingDirectory = TF2_PATH;
                    process.StartInfo.Arguments = $"mks_export.mks mks_export.sht mks_export.tga";
                    process.Start();
                    process.WaitForExit();
                }

                Console.WriteLine("Step 5. Deleting leftover files.");
                // Move the generated SHT and TGA files to the 'usermod\materialsrc' folder. Remove left over files.
                MoveFile($"{TF2_PATH}\\mks_export.sht", $"{TF2_PATH}\\usermod\\materialsrc\\mks_export.sht", true);
                MoveFile($"{TF2_PATH}\\mks_export.tga", $"{TF2_PATH}\\usermod\\materialsrc\\mks_export.tga", true);
                File.Delete($"{TF2_PATH}\\mks_export.mks");

                index = 1;
                // Remove TGA files from the TF2 and application directories.
                foreach (var file in Directory.GetFiles(TF2_PATH, "*.tga", SearchOption.TopDirectoryOnly))
                {
                    File.Delete(file);
                    File.Delete($"frames\\converted\\frame_{index}.tga");
                    index++;
                }

                Console.WriteLine("Step 5. Generating the VTF file.");
                // Call the process to generate the VTF file from the SHT.
                using (var process = new Process())
                {
                    process.StartInfo.FileName = "vtex.exe";
                    process.StartInfo.WorkingDirectory = TF2_PATH;
                    process.StartInfo.Arguments = $"\"{TF2_PATH}\\usermod\\materialsrc\\mks_export.sht\"";
                    process.Start();
                    process.WaitForExit();
                }
                // Move the generated VTF file to the application directory.
                var APP_PATH = Directory.GetCurrentDirectory();
                MoveFile($"{TF2_PATH}\\materials\\mks_export.vtf", $"{APP_PATH}\\frames\\converted\\export.vtf", true);

                // Remove the generated SHT and TGA files. They won't be needed anymore.
                File.Delete($"{TF2_PATH}\\usermod\\materialsrc\\mks_export.sht");
                File.Delete($"{TF2_PATH}\\usermod\\materialsrc\\mks_export.tga");

                // Generate the accompanying VMT file if that flag is checked.
                if (CreateVMT.IsChecked == true)
                {
                    Console.WriteLine("Step 6. Generating VMT file.");
                    lines = new List<string>();
                    lines.Add("\"SpriteCard\"\n");
                    lines.Add("{\n");
                    lines.Add($"	\"$basetexture\"	\"particles/export\"\n");
                    lines.Add($"	\"$translucent\"	\"1\"\n");
                    lines.Add($"	\"$blendframes\"	\"{((BlendFrames.IsChecked == true) ? 1 : 0)}\"\n");
                    lines.Add("}");
                    WriteFile(lines, $"{APP_PATH}\\frames\\converted\\export.vmt");
                }

                Status.Text = "Processing Complete. Please update the VTF file's \"basetexture\" path.";
                Process.Start($"{APP_PATH}\\frames\\converted");
                Console.WriteLine("Done!");
                return;
            }
        }

        /// <summary>
        /// Checks the root application folder for valid PNG images.
        /// </summary>
        private void CheckFile()
        {
            // Create the required folder at the root of the application, if they don't already exist.
            if (!Directory.Exists("frames\\converted")) Directory.CreateDirectory("frames\\converted");

            // Loop through the PNG files in the 'frames' folder, adding them to the list view.
            var index = 0;
            FileList.Items.Clear();
            foreach (var file in Directory.GetFiles("frames\\", "*.png", SearchOption.TopDirectoryOnly))
            {
                FileList.Items.Insert(index, file);
                index++;
            }

            // Update the GUI depending on whether or not valid images were found.
            Convert.IsEnabled = (FileList.Items.Count > 0) ? true : false;
            Status.Text = (Convert.IsEnabled) ? "Ready!" : "No valid images found. Place your numbered PNG image files (0.png, 1.png etc.) into the 'frames' folder.";
        }

        /// <summary>
        /// Moves the file from one directory to another. Deletes the files if necessary.
        /// </summary>
        /// <param name="input">Source file that needs to be moved.</param>
        /// <param name="output">Directory to which the source file will be moved.</param>
        /// <param name="deleteInput">If TRUE, delete the source file after the move.</param>
        public void MoveFile(string input, string output, bool deleteInput = false)
        {
            // Delete the output file if it already exists.
            if (File.Exists(output)) File.Delete(output);

            // Move the input file to the output directory.
            File.Move(input, output);

            // If the flag is checked, remove the input file.
            if (deleteInput) File.Delete(input);
        }

        /// <summary>
        /// Creates a new file with the list of strings provided.
        /// </summary>
        /// <param name="lines">List of strings to write to the file.</param>
        /// <param name="directory">Target file directory to which the lines will be written.</param>
        public void WriteFile(List<string> lines, string directory)
        {
            // Check if the specified file exists. If so, then delete it.
            if (File.Exists(directory)) File.Delete(directory);

            // Write each line into the file specified.
            using (var writer = File.CreateText(directory))
                foreach (var line in lines)
                    writer.WriteLine(line);
        }
    }
}