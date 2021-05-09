﻿using Flowframes.IO;
using Flowframes.Magick;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Padding = Flowframes.Data.Padding;
using I = Flowframes.Interpolate;
using System.Diagnostics;
using Flowframes.Data;
using Flowframes.Media;
using Microsoft.VisualBasic.Logging;

namespace Flowframes.Main
{
    class CreateVideo
    {
        

        public static async Task Export(string path, string outFolder, I.OutMode mode, bool stepByStep)
        {
            if(Config.GetInt("sceneChangeFillMode") == 1)
            {
                string frameFile = Path.Combine(I.current.tempFolder, Paths.GetFrameOrderFilename(I.current.interpFactor));
                await Blend.BlendSceneChanges(frameFile);
            }
            
            if (!mode.ToString().ToLower().Contains("vid"))     // Copy interp frames out of temp folder and skip video export for image seq export
            {
                try
                {
                    await ExportFrames(path, stepByStep);
                }
                catch (Exception e)
                {
                    Logger.Log("Failed to move interpolated frames: " + e.Message);
                }

                return;
            }

            if (IOUtils.GetAmountOfFiles(path, false, "*" + I.current.interpExt) <= 1)
            {
                I.Cancel("Output folder does not contain frames - An error must have occured during interpolation!", AiProcess.hasShownError);
                return;
            }

            await Task.Delay(10);
            Program.mainForm.SetStatus("Creating output video from frames...");

            try
            {
                string max = Config.Get("maxFps");
                Fraction maxFps = max.Contains("/") ? new Fraction(max) : new Fraction(max.GetFloat());
                bool fpsLimit = maxFps.GetFloat() > 0f && I.current.outFps.GetFloat() > maxFps.GetFloat();
                bool dontEncodeFullFpsVid = fpsLimit && Config.GetInt("maxFpsMode") == 0;

                if (!dontEncodeFullFpsVid)
                    await Encode(mode, path, Path.Combine(outFolder, await IOUtils.GetCurrentExportFilename(false, true)), I.current.outFps, new Fraction());

                if (fpsLimit)
                    await Encode(mode, path, Path.Combine(outFolder, await IOUtils.GetCurrentExportFilename(true, true)), I.current.outFps, maxFps);
            }
            catch (Exception e)
            {
                Logger.Log("FramesToVideo Error: " + e.Message, false);
                MessageBox.Show("An error occured while trying to convert the interpolated frames to a video.\nCheck the log for details.");
            }
        }

        static async Task ExportFrames (string framesPath, bool stepByStep)
        {
            Program.mainForm.SetStatus("Copying output frames...");
            string desiredFormat = Config.Get("imgSeqFormat").ToLower();
            string availableFormat = Path.GetExtension(IOUtils.GetFilesSorted(framesPath)[0].Remove(".").ToLower());
            string max = Config.Get("maxFps");
            Fraction maxFps = max.Contains("/") ? new Fraction(max) : new Fraction(max.GetFloat());
            bool fpsLimit = maxFps.GetFloat() > 0f && I.current.outFps.GetFloat() > maxFps.GetFloat();
            bool dontEncodeFullFpsVid = fpsLimit && Config.GetInt("maxFpsMode") == 0;
            string framesFile = Path.Combine(framesPath.GetParentDir(), Paths.GetFrameOrderFilename(I.current.interpFactor));
            

            if (!dontEncodeFullFpsVid)
            {
                string outputFolderPath = Path.Combine(I.current.outPath, await IOUtils.GetCurrentExportFilename(false, false));
                Logger.Log($"Exporting {desiredFormat.ToUpper()} frames to '{Path.GetFileName(outputFolderPath)}'...");

                if (desiredFormat == availableFormat)   // Move as the frames are already in the desired format
                    await CopyOutputFrames(framesPath, framesFile, outputFolderPath, fpsLimit);
                else    // Encode with ffmpeg
                    await FfmpegEncode.FramesToFrames(framesFile, outputFolderPath, I.current.outFps, new Fraction(), desiredFormat);
            }
            
            if (fpsLimit)
            {
                string outputFolderPath = Path.Combine(I.current.outPath, await IOUtils.GetCurrentExportFilename(true, false));
                Logger.Log($"Exporting {desiredFormat.ToUpper()} frames to '{Path.GetFileName(outputFolderPath)}' (Resampled to {maxFps} FPS)...");
                await FfmpegEncode.FramesToFrames(framesFile, outputFolderPath, I.current.outFps, maxFps, desiredFormat);
            }

            if (!stepByStep)
                await IOUtils.DeleteContentsOfDirAsync(I.current.interpFolder);
        }

        static async Task CopyOutputFrames(string framesPath, string framesFile, string outputFolderPath, bool dontMove)
        {
            await IOUtils.TryDeleteIfExistsAsync(outputFolderPath);
            IOUtils.CreateDir(outputFolderPath);
            Stopwatch sw = new Stopwatch();
            sw.Restart();

            string[] framesLines = IOUtils.ReadLines(framesFile);

            for (int idx = 1; idx <= framesLines.Length; idx++)
            {
                string line = framesLines[idx - 1];
                string inFilename = line.RemoveComments().Split('/').Last().Remove("'").Trim();
                string framePath = Path.Combine(framesPath, inFilename);
                string outFilename = Path.Combine(outputFolderPath, idx.ToString().PadLeft(Padding.interpFrames, '0')) + Path.GetExtension(framePath);

                if (dontMove || ((idx < framesLines.Length) && framesLines[idx].Contains(inFilename)))   // If file is re-used in the next line, copy instead of move
                    File.Copy(framePath, outFilename);
                else
                    File.Move(framePath, outFilename);

                if (sw.ElapsedMilliseconds >= 500 || idx == framesLines.Length)
                {
                    sw.Restart();
                    Logger.Log($"Moving output frames... {idx}/{framesLines.Length}", false, true);
                    await Task.Delay(1);
                }
            }
        }

        static async Task Encode(I.OutMode mode, string framesPath, string outPath, Fraction fps, Fraction resampleFps)
        {
            string currentOutFile = outPath;
            string framesFile = Path.Combine(framesPath.GetParentDir(), Paths.GetFrameOrderFilename(I.current.interpFactor));

            if (!File.Exists(framesFile))
            {
                bool sbs = Config.GetInt("processingMode") == 1;
                I.Cancel($"Frame order file for this interpolation factor not found!{(sbs ? "\n\nDid you run the interpolation step with the current factor?" : "")}");
                return;
            }

            if (mode == I.OutMode.VidGif)
            {
                await FfmpegEncode.FramesToGifConcat(framesFile, outPath, fps, true, Config.GetInt("gifColors"), resampleFps);
            }
            else
            {
                VidExtraData extraData = await FfmpegCommands.GetVidExtraInfo(I.current.inPath);
                await FfmpegEncode.FramesToVideo(framesFile, outPath, mode, fps, resampleFps, extraData);
                await MuxOutputVideo(I.current.inPath, outPath);
                await Loop(currentOutFile, await GetLoopTimes());
            }
        }

        public static async Task ChunksToVideos(string tempFolder, string chunksFolder, string baseOutPath)
        {
            if (IOUtils.GetAmountOfFiles(chunksFolder, true, "*" + FFmpegUtils.GetExt(I.current.outMode)) < 1)
            {
                I.Cancel("No video chunks found - An error must have occured during chunk encoding!", AiProcess.hasShownError);
                return;
            }

            await Task.Delay(10);
            Program.mainForm.SetStatus("Merging video chunks...");
            try
            {
                DirectoryInfo chunksDir = new DirectoryInfo(chunksFolder);
                foreach (DirectoryInfo dir in chunksDir.GetDirectories())
                {
                    string suffix = dir.Name.Replace("chunks", "");
                    string tempConcatFile = Path.Combine(tempFolder, $"chunks-concat{suffix}.ini");
                    string concatFileContent = "";

                    foreach (string vid in IOUtils.GetFilesSorted(dir.FullName))
                        concatFileContent += $"file '{Paths.chunksDir}/{dir.Name}/{Path.GetFileName(vid)}'\n";

                    File.WriteAllText(tempConcatFile, concatFileContent);
                    Logger.Log($"CreateVideo: Running MergeChunks() for frames file '{Path.GetFileName(tempConcatFile)}'", true);
                    bool fpsLimit = dir.Name.Contains(Paths.fpsLimitSuffix);
                    string outPath = Path.Combine(baseOutPath, await IOUtils.GetCurrentExportFilename(fpsLimit, true));
                    await MergeChunks(tempConcatFile, outPath);
                }
            }
            catch (Exception e)
            {
                Logger.Log("ChunksToVideo Error: " + e.Message, false);
                MessageBox.Show("An error occured while trying to merge the video chunks.\nCheck the log for details.");
            }
        }

        static async Task MergeChunks(string vfrFile, string outPath)
        {
            await FfmpegCommands.ConcatVideos(vfrFile, outPath, -1);
            await MuxOutputVideo(I.current.inPath, outPath);
            await Loop(outPath, await GetLoopTimes());
        }

        public static async Task EncodeChunk(string outPath, I.OutMode mode, int firstFrameNum, int framesAmount)
        {
            string framesFileFull = Path.Combine(I.current.tempFolder, Paths.GetFrameOrderFilename(I.current.interpFactor));
            string framesFileChunk = Path.Combine(I.current.tempFolder, Paths.GetFrameOrderFilenameChunk(firstFrameNum, firstFrameNum + framesAmount));
            File.WriteAllLines(framesFileChunk, IOUtils.ReadLines(framesFileFull).Skip(firstFrameNum).Take(framesAmount));

            if (Config.GetInt("sceneChangeFillMode") == 1)
                await Blend.BlendSceneChanges(framesFileChunk, false);

            string max = Config.Get("maxFps");
            Fraction maxFps = max.Contains("/") ? new Fraction(max) : new Fraction(max.GetFloat());
            bool fpsLimit = maxFps.GetFloat() != 0 && I.current.outFps.GetFloat() > maxFps.GetFloat();
            VidExtraData extraData = await FfmpegCommands.GetVidExtraInfo(I.current.inPath);

            bool dontEncodeFullFpsVid = fpsLimit && Config.GetInt("maxFpsMode") == 0;

            if (!dontEncodeFullFpsVid)
                await FfmpegEncode.FramesToVideo(framesFileChunk, outPath, mode, I.current.outFps, new Fraction(), extraData, AvProcess.LogMode.Hidden, true);     // Encode

            if (fpsLimit)
            {
                string filename = Path.GetFileName(outPath);
                string newParentDir = outPath.GetParentDir() + Paths.fpsLimitSuffix;
                outPath = Path.Combine(newParentDir, filename);
                await FfmpegEncode.FramesToVideo(framesFileChunk, outPath, mode, I.current.outFps, maxFps, extraData, AvProcess.LogMode.Hidden, true);     // Encode with limited fps
            }
        }

        static async Task Loop(string outPath, int looptimes)
        {
            if (looptimes < 1 || !Config.GetBool("enableLoop")) return;
            Logger.Log($"Looping {looptimes} {(looptimes == 1 ? "time" : "times")} to reach target length of {Config.GetInt("minOutVidLength")}s...");
            await FfmpegCommands.LoopVideo(outPath, looptimes, Config.GetInt("loopMode") == 0);
        }

        static async Task<int> GetLoopTimes()
        {
            int times = -1;
            int minLength = Config.GetInt("minOutVidLength");
            int minFrameCount = (minLength * I.current.outFps.GetFloat()).RoundToInt();
            int outFrames = ((await I.GetCurrentInputFrameCount()) * I.current.interpFactor).RoundToInt();
            if (outFrames / I.current.outFps.GetFloat() < minLength)
                times = (int)Math.Ceiling((double)minFrameCount / (double)outFrames);
            times--;    // Not counting the 1st play (0 loops)
            if (times <= 0) return -1;      // Never try to loop 0 times, idk what would happen, probably nothing
            return times;
        }

        public static async Task MuxOutputVideo(string inputPath, string outVideo)
        {
            if (!File.Exists(outVideo))
            {
                I.Cancel($"No video was encoded!\n\nFFmpeg Output:\n{AvProcess.lastOutputFfmpeg}");
                return;
            }

            if (!Config.GetBool("keepAudio") && !Config.GetBool("keepAudio"))
                return;

            Program.mainForm.SetStatus("Muxing audio/subtitles into video...");

            bool muxFromInput = Config.GetInt("audioSubTransferMode") == 0;

            if (muxFromInput && I.current.inputIsFrames)
            {
                Logger.Log("Skipping muxing from input step as there is no input video, only frames.", true);
                return;
            }

            try
            {
                if (muxFromInput)
                    await FfmpegAudioAndMetadata.MergeStreamsFromInput(inputPath, outVideo, I.current.tempFolder);
                else
                    await FfmpegAudioAndMetadata.MergeAudioAndSubs(outVideo, I.current.tempFolder);
            }
            catch (Exception e)
            {
                Logger.Log("Failed to merge audio/subtitles with output video!");
                Logger.Log("MergeAudio() Exception: " + e.Message, true);
            }
        }
    }
}
