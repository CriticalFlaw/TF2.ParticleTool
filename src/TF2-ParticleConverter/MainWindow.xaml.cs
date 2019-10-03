using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
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
            CheckForFiles();
        }

        /// <summary>
        /// Run the complete image to particle conversion process.
        /// </summary>
        private void RunConversion()
        {
            // Loop through all the PNG images in the 'frames' folder, converting each one to TGA format.
            Status.Text = "";
            var index = 0;
            foreach (var file in GetFileList("frames\\", "png"))
                // Check that the file name is in numeric order, as those are the only ones we'll be processing.
                if (file.Name == $"{index}.png")
                {
                    TGA convert;
                    using (var original = new Bitmap($"frames\\{index}.png"))
                    using (var clone = new Bitmap(original))
                    using (var newbmp = clone.Clone(new Rectangle(0, 0, clone.Width, clone.Height), PixelFormat.Format32bppArgb))
                        convert = (TGA)newbmp;
                    convert.Save($"frames\\converted\\frame_{index + 1}.tga");
                    index++;
                }

            // Check that we've processed some images files. If not, then abort the operation.
            Status.Text = (index == 0) ? Properties.Resources.ERROR_CONVERTION : Status.Text;
            if (!string.IsNullOrWhiteSpace(Status.Text)) return;

            //--------------------------------------------------------------------------------

            // Find the base Steam library path by searching the Registry. Append it with the path to the Team Fortress 2.
            var TF2_PATH = (string)Registry.GetValue((Environment.Is64BitProcess) ? @"HKEY_LOCAL_MACHINE\Software\Wow6432Node\Valve\Steam" : @"HKEY_LOCAL_MACHINE\Software\Valve\Steam", "InstallPath", null);
            TF2_PATH += "\\steamapps\\common\\Team Fortress 2\\bin\\";
            TF2_PATH = (!Directory.Exists(TF2_PATH)) ? FindTF2Directory() : TF2_PATH;

            // Check that the Team Fortress 2 directory exists. If not, then abort the operation.
            Status.Text = (!Directory.Exists(TF2_PATH)) ? Properties.Resources.ERROR_DIRECTORY_TF2 : Status.Text;
            if (!string.IsNullOrWhiteSpace(Status.Text)) return;

            // Create the required 'usermod\materialsrc' directory if it doesn't already exist.
            if (!Directory.Exists(TF2_PATH + "usermod\\materialsrc")) Directory.CreateDirectory(TF2_PATH + "usermod\\materialsrc");

            // Copy the gameinfo.txt file to the 'bin' directory if it doesn't already exist.
            File.Copy(TF2_PATH.Replace("bin", "tf") + "gameinfo.txt", TF2_PATH + "gameinfo.txt", true);

            //--------------------------------------------------------------------------------

            // Create the MKS file with the following header line.
            var lines = new List<string>();
            lines.Add("sequence 0");

            // Add the line 'loop' to the file if that option was checked.
            if (LoopMaterial.IsChecked == true) lines.Add("loop");

            index = 1;
            // Loop through all of the TGA files, recording them in the MKS file then copying them to the TF2 directory.
            foreach (var file in GetFileList("frames\\converted\\", "tga"))
                if (file.Name == $"frame_{index}.tga")
                {
                    lines.Add($"frame frame_{index}.tga 1");
                    File.Copy($"frames\\converted\\frame_{index}.tga", TF2_PATH + $"frame_{index}.tga", true);
                    index++;
                }

            // Get the assigned inputted name, use it to create the MKS file.
            var fileName = (!string.IsNullOrWhiteSpace(ExportName.Text)) ? ExportName.Text : "export";
            var mks = "mks_" + fileName + ".mks";
            var sht = "mks_" + fileName + ".sht";
            var tga = "mks_" + fileName + ".tga";
            var vtf = "mks_" + fileName + ".vtf";

            WriteToFile(lines, TF2_PATH + mks);

            // Check that the MKS file was generated and placed in the bin folder.
            Status.Text = (!File.Exists(TF2_PATH + mks)) ? Properties.Resources.ERROR_CREATE_MKS : Status.Text;
            if (!string.IsNullOrWhiteSpace(Status.Text)) return;

            //--------------------------------------------------------------------------------

            // Call the process to generate SHT and TGA files using the MKS.
            using (var process = new Process())
            {
                process.StartInfo.FileName = "mksheet.exe";
                process.StartInfo.WorkingDirectory = TF2_PATH;
                process.StartInfo.Arguments = $"{mks} {sht} {tga}";
                process.Start();
                process.WaitForExit();
            }

            // Check that the SHT and TGA files were generated and placed in the bin folder.
            Status.Text = (!File.Exists(TF2_PATH + sht) || !File.Exists(TF2_PATH + tga)) ? Properties.Resources.ERROR_CREATE_SHT : Status.Text;
            if (!string.IsNullOrWhiteSpace(Status.Text)) return;

            //--------------------------------------------------------------------------------

            // Move the generated SHT and TGA files to the 'usermod\materialsrc' directory. Remove leftover files.
            File.Delete(TF2_PATH + mks);
            CopyFile(TF2_PATH + sht, TF2_PATH + "usermod\\materialsrc\\" + sht, true);
            CopyFile(TF2_PATH + tga, TF2_PATH + "usermod\\materialsrc\\" + tga, true);

            // Remove the TGA files from the TF2 and application directories.
            index = 1;
            foreach (var file in Directory.GetFiles(TF2_PATH, "*.tga", SearchOption.TopDirectoryOnly))
            {
                File.Delete(file);
                File.Delete($"frames\\converted\\frame_{index}.tga");
                index++;
            }

            //--------------------------------------------------------------------------------

            // Call the process to generate the VTF file from the SHT.
            using (var process = new Process())
            {
                process.StartInfo.FileName = "vtex.exe";
                process.StartInfo.WorkingDirectory = TF2_PATH;
                process.StartInfo.Arguments = $"\"{TF2_PATH}usermod\\materialsrc\\{sht}\"";
                process.Start();
                process.WaitForExit();
            }
            // Move the generated VTF file to the application directory.
            var APP_PATH = Directory.GetCurrentDirectory();
            CopyFile(TF2_PATH + "materials\\" + vtf, APP_PATH + $"\\frames\\converted\\{fileName}.vtf", true);

            // Remove the generated SHT and TGA files.
            File.Delete(TF2_PATH + "usermod\\materialsrc\\" + sht);
            File.Delete(TF2_PATH + "usermod\\materialsrc\\" + tga);

            //--------------------------------------------------------------------------------

            if (CreateVMT.IsChecked == true)
            {
                // Generate the accompanying VMT file if that flag is checked.
                lines = new List<string>
                {
                    "\"SpriteCard\"\n",
                    "{\n",
                    $"	\"$basetexture\"	\"particles/{fileName}\"\n",
                    $"	\"vertexcolor\"	    \"1\"\n",
                    $"	\"vertexalpha\"	    \"1\"\n",
                    $"	\"$translucent\"	\"1\"\n",
                    $"	\"$blendframes\"	\"{((BlendFrames.IsChecked == true) ? 1 : 0)}\"\n",
                    "}"
                };
                WriteToFile(lines, APP_PATH + $"\\frames\\converted\\{fileName}.vmt");
            }

            Status.Text = Properties.Resources.CONVERTION_DONE;
            Process.Start(APP_PATH + "\\frames\\converted");
        }

        /// <summary>
        /// Checks the root application directory for numbered PNG images to process.
        /// </summary>
        private void CheckForFiles()
        {
            // Create the 'frames\converted' directory at the root of the application if they don't already exist.
            if (!Directory.Exists("frames\\converted")) Directory.CreateDirectory("frames\\converted");

            FileList.Items.Clear();
            // Loop through the images files in the 'frames' folder, adding them to the list view.
            foreach (var file in GetFileList("frames\\", "png"))
                FileList.Items.Add("frames\\" + file);

            // Update the GUI depending on whether or not valid images were found.
            Convert.IsEnabled = (FileList.Items.Count > 0) ? true : false;
            Status.Text = (Convert.IsEnabled) ? "Ready!" : Properties.Resources.ERROR_PNG_NOT_FOUND;
        }

        /// <summary>
        /// Moves a given file from one directory to another.
        /// </summary>
        /// <param name="source">Path to the file that needs to be copied.</param>
        /// <param name="target">Directory to which the file will be copied to.</param>
        /// <param name="remove">If TRUE, delete the source file after it has been copied.</param>
        private static void CopyFile(string source, string target, bool remove = false)
        {
            // Copy the source file to the target directory.
            File.Copy(source, target, true);

            // If TRUE, delete the source file.
            if (remove) File.Delete(source);
        }

        /// <summary>
        /// Write a list of strings to a newly created file.
        /// </summary>
        /// <param name="lines">List of strings to write to the file.</param>
        /// <param name="directory">File path to which the lines will be written.</param>
        private static void WriteToFile(List<string> lines, string filePath)
        {
            // Remove the specified file if it already exists.
            if (File.Exists(filePath)) File.Delete(filePath);

            // Write the list of strings to the newly created file.
            using (var writer = File.CreateText(filePath))
                foreach (var line in lines)
                    writer.WriteLine(line);
        }

        /// <summary>
        /// Called when the application wasn't able to find the Team Fortress 2 directory and asks the user to do so instead.
        /// </summary>
        private static string FindTF2Directory()
        {
            using (var browser = new FolderBrowserDialog { Description = Properties.Resources.FOLDER_BROWSE_DESC, ShowNewFolderButton = true })
                while (!browser.SelectedPath.Contains("Team Fortress 2\\bin"))
                {
                    if (browser.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        if (browser.SelectedPath.Contains("Team Fortress 2\\bin"))
                            return browser.SelectedPath;
                    }
                    else
                        break;
                }
            return null;
        }

        public static IOrderedEnumerable<FileSystemInfo> GetFileList(string directory, string extension)
        {
            return new DirectoryInfo(directory).GetFileSystemInfos("*." + extension).OrderBy(x => int.Parse(x.Name.Substring(x.Name.Contains("frame_") ? 6 : 0, (x.Name.Contains("frame_") ? x.Name.Length - 6 : x.Name.Length) - x.Extension.Length)));
        }

        private void Convert_Click(object sender, RoutedEventArgs e)
        {
            RunConversion();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            CheckForFiles();
        }

        private void FileList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Process.Start(Directory.GetCurrentDirectory() + "\\" + FileList.Items.GetItemAt(FileList.SelectedIndex));
        }
    }
}