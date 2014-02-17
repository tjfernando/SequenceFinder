using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DnsSequenceSearcher
{
    class Program
    {
        #region Fields

        private static Dictionary<string, string> arguments = new Dictionary<string, string>();
        private static string searchString = string.Empty;
        private static StringBuilder outPut = new StringBuilder();
        private static string outputDirectory;
        private static string inputFileName;
        private static Object locker = new Object();
        private static int searchStringLength;
        private static int chunkingSize = 0;
        private static ConcurrentBag<string> outPutFromTasks = new ConcurrentBag<string>();
        private static Dictionary<int, int> shiftValues = new Dictionary<int, int>();
        private static List<string> possibleTwoCharacterCombinations = new List<string>()
                                                                {
                                                                    "AA", "AC", "AT", "AG",
                                                                    "CC", "CA", "CT", "CG",
                                                                    "TT", "TA", "TC", "TG",
                                                                    "GG", "GA", "GC", "GT",
                                                                };
        private static int searchStringEnd = 0;

        #endregion

        #region Methods

        static void Main(string[] args)
        {
            if(args.Length > 0)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    var argument = args[i];
                    switch (argument)
                    {
                        case "/f":
                        case "/o":
                        case "/c":
                        case "/s":
                        case "/I":
                        case "/S":
                            arguments.Add(argument, args[++i]);
                            break;
                        default:
                            Console.WriteLine("Unknown argument {0}\nUsage: /f [InputFileName] /o [OutputDirectory] /c [ChunkingSize] /s [SearchSequence] *[/I CreateInputFileName] *[/S SizeOfInputFile]", argument);
                            return;
                    }
                }
            }
            else
            {
                Console.WriteLine("Usage: /f [InputFileName] \n       /o [OutputDirectory] \n       /c [ChunkingSize] \n       /s [SearchSequence] \n       *[/I CreateInputFileName] *[/S SizeOfInputFile]");
                return;
            }

            Stopwatch stopwatch = new Stopwatch();
            if(arguments.ContainsKey("/I") && arguments.ContainsKey("/S"))
            {
                var inputFileName = arguments["/I"];
                var fileSize = 0;
                try
                {
                    fileSize = Convert.ToInt32(arguments["/S"]);
                }
                catch(Exception)
                {
                    Console.WriteLine("Unsupplied argument /S [ChunkingSize]");
                    return;
                }

                LogOutput(string.Format("Started creating input file at: {0:yyyy/MM/dd HH:mm:ss.fff}\r\n", DateTime.Now));
                stopwatch.Start();
                CreateTestFile(inputFileName, fileSize);
                stopwatch.Stop();
            }
            else
            {
                if(!ParseMatchArguments()) return;

                LogOutput(string.Format("\r\nSearching for {0} with chunking size of {1}\r\n", searchString, chunkingSize));
                LogOutput(string.Format("Started at: {0:yyyy/MM/dd HH:mm:ss.fff}\r\n", DateTime.Now));
                stopwatch.Start();
                
                searchStringLength = searchString.Length;
                searchStringEnd = searchStringLength - 1;

                CalculateShiftValues();
                FindMatches();

                stopwatch.Stop();

                foreach (var item in outPutFromTasks)
                {
                    if (!string.IsNullOrEmpty(item))
                    {
                        LogOutput(item);
                    }
                }
                LogOutput(outPut.ToString());
            }

            Console.WriteLine("Total Processing Time: {0}ms", stopwatch.ElapsedMilliseconds);
            LogOutput(string.Format("Finished at: {0:yyyy/MM/dd HH:mm:ss.fff} - Time Taken: {1}ms\r\n", DateTime.Now, stopwatch.ElapsedMilliseconds));
        }

        private static void FindMatches()
        {
            FileStream fileStream = new FileStream(inputFileName, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);

            List<Task> tasks = new List<Task>();
            var outPutFromTasks = new ConcurrentBag<string>();

            while (fileStream.Position < fileStream.Length)
            {
                double tempPosition = fileStream.Position;
                Task t = Task.Run(() => GetProcessFileChunkResult(ref fileStream));
                tasks.Add(t);
                Thread.Sleep(5);
                lock (locker)
                {
                    if (fileStream.Position != fileStream.Length)
                    {
                        fileStream.Position = fileStream.Position - (searchStringLength - 1);
                    }
                }
            }
            Thread.Sleep(0);
            Task.WaitAll(tasks.ToArray());
        }
        
        private static string ProcessFileChunk(ref FileStream fileStream, int chunkingSize)
        {
            int count;
            byte[] buffer = new byte[chunkingSize];
            double fileStreamPosition;

            lock (locker)
            {
                count = fileStream.Read(buffer, 0, buffer.Length);
                fileStreamPosition = fileStream.Position;
            }

            StringBuilder outPut = new StringBuilder();
            #if DEBUG
                outPut.AppendLine(string.Format("Read: {0} characters to {1}", count, fileStream.Position));
            #endif
            var currentWindowStart = 0;
            var currentWindowEnd = currentWindowStart + searchStringEnd;

            while (true)
            {
                #if DEBUG
                outPut.AppendLine(string.Format("Checking: {0} to {1}", currentWindowStart, currentWindowEnd));
                outPut.AppendLine(string.Format("-Checking: {0} vs {1}", searchString[searchStringEnd], Convert.ToChar(buffer[currentWindowEnd])));
                #endif

                if (searchString[searchStringEnd] == Convert.ToChar(buffer[currentWindowEnd]))
                {
                    #if DEBUG
                    outPut.AppendLine(string.Format("--Checking: {0} vs {1}", searchString[0], Convert.ToChar(buffer[currentWindowStart])));
                    #endif
                    if (searchString[0] == Convert.ToChar(buffer[currentWindowStart]))
                    {
                        var foundMatch = true;
                        for (int i = searchStringLength - 1; i > 0; i--)
                        {
                            #if DEBUG
                            outPut.AppendLine(string.Format("---Checking: {0} vs {1}", searchString[i], Convert.ToChar(buffer[currentWindowEnd - (searchStringLength - 1 - i)])));
                            #endif
                            if (searchString[i] != Convert.ToChar(buffer[currentWindowEnd - (searchStringLength - 1 - i)]))
                            {
                                foundMatch = false;
                                break;
                            }
                        }

                        if (foundMatch)
                        {
                            outPut.AppendLine(string.Format("Found Match position: {0}", fileStreamPosition - count + currentWindowStart));
                        }
                    }
                }

                if (currentWindowEnd + 2 <= count - 1)
                {
                    var firstCharacterAfterWindow = Convert.ToChar(buffer[currentWindowEnd + 1]);
                    var secondChatacterAfterWindow = Convert.ToChar(buffer[currentWindowEnd + 2]);
                    var shiftCount = shiftValues[(firstCharacterAfterWindow << 5) ^ secondChatacterAfterWindow];
                    #if DEBUG
                    outPut.AppendLine(string.Format("** Shifting By: {0}:Passed In: {1} & {2}\n", shiftCount, firstCharacterAfterWindow, secondChatacterAfterWindow));
                    #endif
                    currentWindowStart = currentWindowStart + shiftCount;
                    currentWindowEnd = currentWindowStart + searchStringLength - 1;
                }
                else
                {
                    currentWindowEnd = buffer.Length;
                }

                if (currentWindowEnd > count - 1)
                {
                    break;
                }
            }

            return outPut.ToString();
        }
        
        /// <summary>
        /// Create a dictionary of shift values
        /// </summary>
        private static void CalculateShiftValues()
        {
            Parallel.ForEach(possibleTwoCharacterCombinations, (x) => { shiftValues.Add((x[0] << 5) ^ x[1], CalculateShiftValue(x[0], x[1])); });
        }
        
        private static void GetProcessFileChunkResult(ref FileStream fileStream)
        {
            var returnValue = ProcessFileChunk(ref fileStream, chunkingSize);
            if(!string.IsNullOrEmpty(returnValue))
            {
                outPutFromTasks.Add(returnValue);
            }
        }

        #endregion

        #region HelperMethods

        /// <summary>
        /// Calculate the shift values for a character pair
        /// This could be performed in parallel.
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        private static int CalculateShiftValue(char a, char b)
        {
            var currentShift = searchStringLength + 2;
            if (searchString[searchStringLength - 1] == a)
            {
                return currentShift = 1;
            }

            for (int i = searchStringLength - 2; i >= 0; i--)
            {
                if (searchString[i] == a && searchString[i + 1] == b)
                {
                    var newShift = searchStringLength - (i + 1) + 1;
                    if (newShift < currentShift)
                    {
                        currentShift = newShift;
                        break;
                    }
                }
            }

            if (searchString[0] == b)
            {
                if (searchStringLength + 1 < currentShift)
                {
                    currentShift = searchStringLength + 1;
                }
            }

            return currentShift;
        }
        
        private static bool ParseMatchArguments()
        {
            var returnValue = true;
            if (arguments.ContainsKey("/f"))
            {
                inputFileName = arguments["/f"];
            }
            else
            {
                Console.WriteLine("Unsupplied argument /f [InputFileName]");
                returnValue = false;
            }

            if (arguments.ContainsKey("/o"))
            {
                outputDirectory = arguments["/o"];
            }
            else
            {
                Console.WriteLine("Unsupplied argument /o [OutputDirectory]");
                returnValue = false;
            }

            if (arguments.ContainsKey("/s"))
            {
                searchString = arguments["/s"];
            }
            else
            {
                Console.WriteLine("Unsupplied argument /s [SearchSequence]");
                returnValue = false;
            }

            if (arguments.ContainsKey("/c"))
            {
                try
                {
                    chunkingSize = Convert.ToInt32(arguments["/c"]);
                }
                catch (Exception)
                {
                    Console.WriteLine("Incorrectly supplied argument /c {0}", arguments["/c"]);
                    returnValue = false;
                }
            }
            else
            {
                Console.WriteLine("Unsupplied argument /c [ChunkingSize]");
                returnValue = false;
            }

            return returnValue;
        }

        private static void LogOutput(string message)
        {
            File.AppendAllText(string.Format(@"{0}\DNASearchOutput({1:yyyyMMdd}).txt", outputDirectory, DateTime.Now), message);
        }
        
        #endregion

        #region CreateTestFile

        private static void CreateTestFile(string fileName, double size)
        {
            Random r = new Random();
            char[] characters = new char[4] { 'A', 'C', 'T', 'G' };
            if (File.Exists(fileName))
            {
                File.Delete(fileName);
            }

            using (FileStream fs = File.OpenWrite(fileName))
            {
                StringBuilder sb = new StringBuilder(10000000);
                for (double i = 0; i < size; i++)
                {
                    sb.Append(characters[r.Next(4)]);
                    if (i % 10000000 == 0)
                    {
                        WriteToFileAsync(sb, fs);
                    }
                }
                if (sb.Length > 0)
                {
                    WriteToFileAsync(sb, fs);
                }
            }

            LogOutput(string.Format("Created file of {0} characters\r\n", size));
        }

        private static void WriteToFileAsync(StringBuilder sb, FileStream fs)
        {
            Byte[] info = new ASCIIEncoding().GetBytes(sb.ToString());
            fs.Write(info, 0, info.Length);
            fs.FlushAsync();
            sb.Clear();
        }

        #endregion
    }
}
