using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;

namespace Excelian.Sequence.Processor
{
    class Program
    {
        private const string Filename = @".\dna_sequence.txt";
        private const string MatchString = "ACCTGACTGAACCTGC";
        private const int ChunkSize = 20000000;

        private static readonly List<long> BaseIndexes = new List<long>();

        private static long _startPosition;
        private static long _viewSize;

        static void Main()
        {
            DateTime start = DateTime.Now;

            Console.WriteLine("{0} START", start.ToString("MMM dd HH:mm:ss.fff"));
            Console.WriteLine();

            ProcessFile();

            foreach (var baseIndex in BaseIndexes)
            {
                Console.WriteLine("FOUND SEQUENCE STARTING AT {0}", baseIndex);
            }

            DateTime end = DateTime.Now;

            Console.WriteLine();
            Console.WriteLine("{0} FINISH", end.ToString("MMM dd HH:mm:ss.fff"));
            Console.WriteLine();
            Console.WriteLine("TOTAL EXECUTION TIME {0}", (end - start));

            Console.WriteLine();
            Console.WriteLine("Hit Enter to Exit");
            Console.ReadLine();
        }

        private static void ProcessFile()
        {
            using (var file = new FileStream(Filename, FileMode.Open, FileAccess.Read))
            {
                MemoryMappedFile memoryMappedFile = MemoryMappedFile.CreateFromFile(file, "SequenceMap", file.Length, MemoryMappedFileAccess.Read, null, HandleInheritability.None, false);

                try
                {
                    do
                    {
                        long remainingSize = file.Length - _startPosition;

                        _viewSize = remainingSize > ChunkSize ? ChunkSize : remainingSize;

                        using (MemoryMappedViewStream memoryMappedViewStream = memoryMappedFile.CreateViewStream(_startPosition, _viewSize, MemoryMappedFileAccess.Read))
                        {
                            _startPosition += _viewSize;

                            ProcessChunk(memoryMappedViewStream);
                        }
                    } while (_startPosition < file.Length);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
        }

        private static void ProcessChunk(MemoryMappedViewStream memoryMappedViewStream)
        {
            using (var streamReader = new StreamReader(memoryMappedViewStream))
            {
                while (!streamReader.EndOfStream)
                {
                    var buffer = new char[_viewSize];

                    streamReader.ReadBlock(buffer, 0, buffer.Length);

                    var searchString = new string(buffer);

                    IList<int> currentIndexes = AllIndexesOf(searchString, MatchString);

                    if (currentIndexes.Any())
                    {
                        BaseIndexes.AddRange(currentIndexes.Select(currentIndex => currentIndex + _startPosition));
                    }
                }
            }
        }

        private static IList<int> AllIndexesOf(string searchString, string matchString)
        {
            var indexes = new List<int>();

            for (int index = 0; ; index += matchString.Length)
            {
                index = searchString.IndexOf(matchString, index, StringComparison.Ordinal);

                if (index == -1)
                    return indexes;

                indexes.Add(index);
            }
        }
    }
}
