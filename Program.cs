﻿using System;
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
        private static string handbrakepath;

        private static List<string> target_exts = new List<string>() {"mkv", "avi", "mp4", "wmv", "m2ts", "mov", "qt"};

        private static void CheckAndEnc(string filepath)
        {
            int bitrate;
            string codec = GetCodec(filepath, out bitrate);
            if (codec == "hevc")
            {
                Console.WriteLine(DateTime.Now + "; " + new FileInfo(filepath).Name + " = already hevc");
                return;
            }
            TryQualityEncode(filepath, codec, bitrate);
        }

        private static void TryQualityEncode(string filepath, string codec, int bitrate)
        {
            string handbrakepathtemp = new FileInfo(filepath).Directory + @"\temp.mkv";
            try
            {
                File.Delete(handbrakepathtemp);
            }
            finally
            {
            }

            var lastdate = DateTime.Now;
            var lastsize = new FileInfo(filepath).Length;
            Console.WriteLine(DateTime.Now + "; " + new FileInfo(filepath).Name + " = " + (lastsize / 1024 / 1024) +
                              "mb try enc `" + codec + "` to `hevc`");
            Encode(filepath, handbrakepathtemp, "nvenc_h265", bitrate);
            Console.WriteLine(new FileInfo(filepath).Name + " = end hevc");
            int scsize;
            codec = GetCodec(handbrakepathtemp, out scsize);

            if (codec != "hevc")
            {
                Console.WriteLine(new FileInfo(filepath).Name + " = failed enc to hevc");
                return;
            }

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
                return;
            }

            File.Move(handbrakepathtemp, filepath);

            Console.WriteLine(new FileInfo(filepath).Name +
                              " = succeeded enc to hevc(" + bitrate + "). " + " secs, " + (lastsize / 1024 / 1024));
        }

        private static void Encode(string source, string dest, string codec, int bitrate)
        {
            string output, err;
            RunShell(handbrakepath,
                @"-i """ + source + @""" -o """ + dest + @""" -f av_mkv -e " + codec + " -2 -b " + bitrate, out output,
                out err);
        }

        private static void RunShell(string comm, string args, out string output, out string error)
        {
            Console.WriteLine("RunShell comm=" + comm + "  args=" + args);

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

        private static string GetCodec(string path, out int bitrate)
        {
            bitrate = 0;
            string output = "";
            string err = "";

            RunShell(handbrakepath, @"--scan -i """ + path + @"""",
                out output, out err);

            string result = output + "\r\n" + err;

            string codec = "";
            try
            {
                codec = GetFirstToken(SubstringFindNext(SubstringFindNext(
                    result, "Stream #0:0"), "Video: "));

                string time = GetFirstToken(SubstringFindNext(result, "Duration: "))
                    .Replace(",","").Trim();
                Console.WriteLine("time="+time);
                long sec = int.Parse(time.Substring(0, 2)) * 3600 +
                    int.Parse(time.Substring(3, 2)) * 60 +
                    int.Parse(time.Substring(6, 2));

                bitrate = (int)(new FileInfo(path).Length / 1024 * 8 / sec / 3);
            }
            catch (Exception e)
            {
                // ignored
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
            return str.Substring(str.IndexOf(ch) + 1);
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
            try
            {
                handbrakepath = new FileInfo(System.Reflection.Assembly.GetEntryAssembly().Location).Directory + @"\HandBrakeCLI.exe";
                if (File.Exists(handbrakepath) == false)
                {
                    handbrakepath = Directory.GetCurrentDirectory() + @"\HandBrakeCLI.exe";
                }

                if (File.Exists(handbrakepath) == false)
                {
                    Console.WriteLine("handbrake is not exist");
                    throw new Exception();
                }
                
                var lastdate = DateTime.Now;
                Console.WriteLine();
                Console.WriteLine(lastdate + "; Start autoenc");

                foreach (var p in args)
                {
                    FindAndEnc(p);
                }

                var curdate = DateTime.Now;
                Console.WriteLine(curdate + "; End autoenc = " + (curdate - lastdate).Duration().TotalSeconds +
                                  " secs");
            }
            catch (Exception e)
            {
                throw new Exception("", e);
            }
            finally
            {
                Console.WriteLine("Finished");
                Console.ReadLine();
            }
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