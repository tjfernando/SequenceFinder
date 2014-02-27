using System;
using System.IO;

namespace Excelian.Sequence.Generator
{
    class Program
    {
        static void Main()
        {
            const string nucleobases = "A,C,T,G";

            var bases = nucleobases.Split(new[] { ',' });

            using (var writer = new StreamWriter(@".\dna_sequence.txt", false))
            {
                var rnd = new Random();

                for (double i = 0; i < 3000000000; i++)
                {
                    var index = rnd.Next(0, 4);

                    writer.Write(bases[index]);
                }

                writer.Close();
            }

            Console.WriteLine("Done!");
            Console.ReadLine();
        }
    }
}
