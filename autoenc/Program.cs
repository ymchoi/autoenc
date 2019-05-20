using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace autoenc
{
    internal class Program
    {
        private static List<string> target_pathes = new List<string>()
        {
            @"E:\media_normal",
        };
        private const string complete_file_list = @"e:\temp\enc_already_proceed_files.txt";
        private const string tempdest = @"e:\temp\autoenc.mkv";
        private static string hanebrakepath = @"C:\app\HandBrakeCLI\HandBrakeCLI.exe";

        
        private static List<string> target_exts = new List<string>(){"mkv", "avi", "mp4", "wmv", "m2ts", "mov", "qt"};
        private static HashSet<string> already_proceed_files = new HashSet<string>();

        private static long totaldecrease = 0;
        
        private static void Update_already_proceed_files_path()
        {
            HashSet<string> here_already_proceed_files;
            HashSet<string> here_already_proceed_files_copy = new HashSet<string>();
            try
            {
                here_already_proceed_files = new HashSet<string>(
                    File.ReadAllLines(complete_file_list, Encoding.UTF8));
            }
            catch (Exception e)
            {
                here_already_proceed_files = new HashSet<string>();
            }

            foreach (var f in here_already_proceed_files)
            {
                if (File.Exists(f))
                {
                    here_already_proceed_files_copy.Add(f);
                }
                else
                {
                    Console.WriteLine("file cache removed = " + f);
                }
            }
        
            File.WriteAllLines(complete_file_list, here_already_proceed_files_copy, Encoding.UTF8);
        }
        
        private static void CheckAndEnc(string filepath)
        {
            if (already_proceed_files.Contains(filepath))
            {
                return;
            }

            int scsize;
            string codec = GetCodec(filepath, out scsize);
            if (codec == "hevc")
            {
                Console.WriteLine(DateTime.Now + "; " + new FileInfo(filepath).Name + " = already hevc");
                Refresh_already_proceed_files(filepath);
                return;
            }
            else
            {
                int result;
                
                result = TryQualityEncode(filepath, codec, 22);
                if (result > 22)
                {
                    if (result > 34)
                    {
                        result = 34;
                    }
                    result = TryQualityEncode(filepath, codec, result);
                    Refresh_already_proceed_files(filepath);
                }
            }
        }

        private static int TryQualityEncode(string filepath, string codec, int quality)
        {
                try { File.Delete(tempdest); } finally{}

                var lastdate = DateTime.Now;
                var lastsize = new FileInfo(filepath).Length;
                Console.WriteLine(DateTime.Now + "; " + new FileInfo(filepath).Name + " = "+(lastsize/1024/1024)+"mb try enc `"+ codec +"` to `hevc` " + quality);
                Encode(filepath, tempdest, "nvenc_h265", quality);
                Console.WriteLine(new FileInfo(filepath).Name + " = end hevc");
                int scsize;
                codec = GetCodec(tempdest, out scsize);

                if (codec == "hevc")
                {
                    var curdate = DateTime.Now;
                    var totalsec = (curdate - lastdate).Duration().TotalSeconds;
                    var cursize = new FileInfo(tempdest).Length;
                    if (lastsize < cursize)
                    {
                        Console.WriteLine(curdate + "; " + new FileInfo(filepath).Name + 
                                          " = skip enc to hevc("+quality+"). " + totalsec + " secs, because " + 
                                          (lastsize/1024/1024) + " megabytes => " + (cursize/1024/1024) + " megabytes");
                        float m;
                        if (scsize < 1000000)
                        {
                            m = 0.60f;
                        } else if (scsize > 4000000)
                        {
                            m = 0.45f;
                        }
                        else
                        {
                            m = 0.5f;
                        }
                        float p = ((float) lastsize * m) / (float) cursize;
                        
                        return (int)(Math.Log10(p) / Math.Log10(0.91f) + 22.0f);
                    }
                    else
                    {

                        try
                        {
                            if ((File.GetAttributes(filepath) & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                            {
                                File.SetAttributes(filepath, FileAttributes.Normal);
                            }
                            File.Delete(filepath);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                            Console.WriteLine(new FileInfo(filepath).Name + " = failed enc to hevc");
                            return 0;
                        }
                        File.Move(tempdest, filepath);
                        Refresh_already_proceed_files(filepath);
                        Console.WriteLine(curdate + "; " + new FileInfo(filepath).Name + 
                                          " = succeeded enc to hevc("+quality+"). " + totalsec + " secs, " + 
                                          (lastsize/1024/1024) + " megabytes => " + (cursize/1024/1024) + " megabytes");
                        totaldecrease += (long) (lastsize - cursize);
                    } 
                }
                else
                {
                    Console.WriteLine(new FileInfo(filepath).Name + " = failed enc to hevc");
                }
                return 0;
        }

        private static void Encode(string source, string dest, string codec, int quality)
        {
            string output, err;
            RunShell(handbrakepath,
                @"-i """+source+@""" -o """+dest+@""" -f av_mkv -e "+codec + " -q " + quality, out output, out err);
        }

        private static void Readconfig_already_proceed_files()
        {
            try
            {
                already_proceed_files = new HashSet<string>(
                    File.ReadAllLines(complete_file_list, Encoding.UTF8));
            }
            catch (Exception e)
            {
                already_proceed_files = new HashSet<string>();
            }
        }
        
        
        private static void Refresh_already_proceed_files(string filepath)
        {
            already_proceed_files.Add(filepath);
            File.WriteAllLines(complete_file_list, already_proceed_files, Encoding.UTF8);
        }

        private static void RunShell(string comm, string args, out string output, out string error)
        {
//            Console.WriteLine("RunShell comm=" + comm + "  args=" + args);

            output = "";
            error = "";

            try
            {
                var p = new Process();
                // Redirect the output stream of the child process.
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.FileName = comm;
                p.StartInfo.Arguments = args;
                p.Start();
                p.PriorityClass = ProcessPriorityClass.BelowNormal;

                var taskOut = p.StandardOutput.ReadToEndAsync();
                var taskErr = p.StandardError.ReadToEndAsync();
                while (true)
                {
                    Thread.Sleep(100);
                    if (taskErr.IsCanceled || taskErr.IsFaulted || taskErr.IsCompleted ||
                        taskOut.IsCanceled || taskOut.IsFaulted || taskOut.IsCompleted)
                    {
                        output = taskOut.Result;
                        error = taskErr.Result;
                        break;
                    }
                }

                p.Close();
                
            }
            catch (Exception e)
            {
                error = e.ToString();
            }
        }

        private static string GetCodec(string path, out int size)
        {
            size = 0;
            string output = "";
            string err = "";

            RunShell(@"C:\app\HandBrakeCLI\HandBrakeCLI.exe", @"--scan -i """ + path + @"""",
                out output, out err);
            
            string result = output + "\r\n" + err;

            string codec = "";
            try
            {
                codec = GetFirstToken(SubstringFindNext(SubstringFindNext(
                    result, "Stream #0:0"), "Video: "));
                string sizestring = GetFirstToken(SubstringFindNext(result, "+ size: "));
                sizestring = sizestring.Replace(",", "");
                int size_x = int.Parse(GetFirstToken(sizestring, "x"));
                int size_y = int.Parse(GetSecondToken(sizestring, "x"));
                size = size_x * size_y;
            }
            catch (Exception e)
            {
            }

            return codec;
        }

        private static string GetFirstToken(string str)
        {
            return str.Substring(0, str.IndexOf(" "));
        }
        private static string GetFirstToken(string str, string ch)
        {
            return str.Substring(0, str.IndexOf(ch));
        }
        private static string GetSecondToken(string str, string ch)
        {
            return str.Substring( str.IndexOf(ch)+1);
        }
        
        private static string SubstringFindNext(string str, string target)
        {
            int idx = str.IndexOf(target);
            if (idx < 0)
            {
                throw new Exception();
            }
            return str.Substring(idx + target.Length);

        } 

        public static void Main(string[] args)
        {
            Update_already_proceed_files_path();
        
            var lastdate = DateTime.Now;
            Console.WriteLine();
            Console.WriteLine(lastdate + "; Start autoenc");
            Readconfig_already_proceed_files();
            foreach (var p in target_pathes)
            {
                FindAndEnc(p);
            }
            var curdate = DateTime.Now;
            Console.WriteLine(curdate + "; End autoenc = " + (curdate-lastdate).Duration().TotalSeconds + " secs");
            Console.WriteLine("totaldecrease = " + totaldecrease/1024/1024 + " megabytes");
        }


        private static List<string> DirectoryGetMovieFiles(string target_path)
        {
            List<string> files = new List<string>();
            foreach (var ext in target_exts)
            {
                files.AddRange(Directory.GetFiles(target_path, "*." + ext));
            }
            return files;
        }

        private static void FindAndEnc(string target_path)
        {
            var files = DirectoryGetMovieFiles(target_path);
            foreach (var file in files)
            {
                CheckAndEnc(file);
            }

            foreach (var dir in Directory.GetDirectories(target_path))
            {
                FindAndEnc(dir);
            }
        }
    }
}