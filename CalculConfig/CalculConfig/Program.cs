using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace CalculConfig
{
    class Program
    {
        /// <summary>
        /// Computes the standard deviation of a list of double
        /// </summary>
        /// <param name="sequence"></param>
        /// <returns></returns>
        static double computeSD(IEnumerable<double> sequence)
        {
            double result = 0;

            if (sequence.Any())
            {
                double average = sequence.Average();
                double sum = sequence.Sum(d => Math.Pow(d - average, 2));
                result = Math.Sqrt((sum) / (sequence.Count() - 1));
            }
            return result;
        }

        /// <summary>
        /// Main program
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            //Retrieves the signals from the LSL file
            string basisPath = Directory.GetCurrentDirectory();
            //Console.WriteLine(basisPath);

            //Retrieves the signals from the LSL file
            string[] basisPathSplit = basisPath.Split('\\');
            int indexOrigineFolder = Array.IndexOf(basisPathSplit, "DémosNFsport");
            string[] originPath = new string[indexOrigineFolder + 1];
            Array.Copy(basisPathSplit, originPath, indexOrigineFolder + 1);

            string filesPath = String.Join('\\', originPath) + "\\ScenariiOpenViBE\\signals\\";
            //Console.WriteLine(filesPath);

            using (var reader = new StreamReader(filesPath + "baseline.csv"))
            {
                //Init the lists that will contain the SMR value
                bool firstLine = true;
                List<double> SMRvaluesInFile = new List<double>();
                List<double> SMRgoodValues = new List<double>();

                //Go through all the lines in the file
                while (!reader.EndOfStream)
                {
                    //If it is the first line, pass
                    if (firstLine) { 
                        reader.ReadLine();
                        firstLine = false;
                    }
                    //Read the line and accès the SMR value on [2] and the time of acquisition on [0]
                    var line = reader.ReadLine();
                    var SMRvalue = double.Parse(line.Split(',')[2], System.Globalization.CultureInfo.InvariantCulture);
                    var timeValue = double.Parse(line.Split(',')[0], System.Globalization.CultureInfo.InvariantCulture);

                    //If time of acquisition between 5 and 35 seconds
                    if (timeValue > 5 & timeValue < 35) {
                        //Add the value to the list of values to keep
                        SMRvaluesInFile.Add(SMRvalue);
                    }
                }

                //Compute the mean and standard deviation on the data from the list 
                double mean = SMRvaluesInFile.Average();
                double SD = computeSD(SMRvaluesInFile);

                //Go through the values in the list to remove the outliers
                foreach (double value in SMRvaluesInFile)
                {
                    //If the value is not an outlier, add it to the list of values to keep
                    if (value < mean + 2 * SD & value > mean - 2 * SD)
                    {
                        SMRgoodValues.Add(value);
                    }
                }
                //Compute the mean and standard deviation on the data from the list without the outliers
                double meanGood = SMRgoodValues.Average();
                double SDgood = computeSD(SMRgoodValues);

                //Write the mean and SD in the console and in the config.txt file
                Console.WriteLine(meanGood.ToString() + "   " + SDgood.ToString());
                File.WriteAllText(filesPath + "config.txt", "Mean\n" + meanGood.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + "SD\n" + SDgood.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }
}
