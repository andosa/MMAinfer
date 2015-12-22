/* 
Model for inferring skills for MMA fighters based on match data from 
Sherdog fight finder (http://www.sherdog.com/stats/fightfinder).

Based on Chess Analysis code from http://research.microsoft.com/en-us/um/cambridge/projects/infernet/docs/Chess%20Analysis.aspx
 
*/
using System;
using System.Collections.Generic;
using System.Linq;
using MicrosoftResearch.Infer.Models;
using MicrosoftResearch.Infer.Utils;
using MicrosoftResearch.Infer.Distributions;
using MicrosoftResearch.Infer.Maths;
using System.IO;

namespace MicrosoftResearch.Infer.Tutorials
{

    public class MMASkillModel
    {
        
        public static void Main()
        {
            MMASkillModel mma = new MMASkillModel();
            mma.Run();
        }

        public void LoadData(out int[][] fighter1Data, out int[][] fighter2Data, out int[][] outcomeData, out int[][] winTypeData,
                                         out int[] firstYearData, out int[] lastYearData, out int nFighters, out int nYears, out int startYear)
        {
            Dictionary<string, int> outcomeMap = new Dictionary<string, int>();
            Dictionary<int, List<int>> fighterYears = new Dictionary<int, List<int>>();
            outcomeMap["loss"] = 0;
            outcomeMap["draw"] = 1;
            outcomeMap["win"] = 2;

            string filePath = @"fight_data.csv";
            StreamReader sr = new StreamReader(filePath);
            var lines = new List<string[]>();
            var fighters1 = new List<int>();
            var fighters2 = new List<int>();
            var outcome = new List<int>();
            var wintype = new List<int>();
            var years = new List<int>();

            int Row = 0;
            nFighters = 0;
            while (!sr.EndOfStream)
            {
                string[] Line = sr.ReadLine().Split('\t');
                if (Line[3].Contains("DQ"))
                {
                    continue;
                }

                if (!outcomeMap.ContainsKey(Line[2]))
                {
                    continue;
                }
                int year;

                try
                {
                    year = Int32.Parse(Line[6].Split(' ')[4]);
                    years.Add(year);
                }
                catch (Exception)
                {
                    Console.WriteLine("Error {0}", Row);
                    Console.WriteLine(Line);
                    continue;
                }

                if (Line[3].Contains("KO") || Line[3].Contains("Submission"))
                {
                    wintype.Add(1);
                }
                else
                {
                    wintype.Add(0);
                }


                if (!fighterToKey.ContainsKey(Line[0]))
                {
                    fighterToKey[Line[0]] = nFighters;
                    keyToFighter[nFighters] = Line[0];
                    fighterYears[nFighters] = new List<int>();
                    nFighters++;
                }

                if (!fighterToKey.ContainsKey(Line[1]))
                {
                    fighterToKey[Line[1]] = nFighters;
                    keyToFighter[nFighters] = Line[1];
                    fighterYears[nFighters] = new List<int>();
                    ;
                    nFighters++;
                }

                fighters1.Add(fighterToKey[Line[0]]);
                fighterYears[fighterToKey[Line[0]]].Add(year);
                fighters2.Add(fighterToKey[Line[1]]);

                fighterYears[fighterToKey[Line[1]]].Add(year);

                outcome.Add(outcomeMap[Line[2]]);
                lines.Add(Line);
                Row++;
            }


            var yearArr = years.ToArray();
            var minYear = yearArr.Min();
            var maxYear = yearArr.Max();
            startYear = minYear;

            var yearNormed = (from y in yearArr select (y - minYear)).ToArray();

            nYears = maxYear - minYear + 1;

            var fighter1 = Util.ArrayInit(nYears, year => new List<int>());
            var fighter2 = Util.ArrayInit(nYears, year => new List<int>());
            var outcomes = Util.ArrayInit(nYears, year => new List<int>());
            var winTypes = Util.ArrayInit(nYears, year => new List<int>());

            for (int i = 0; i < fighters1.Count; i++)
            {
                fighter1[yearNormed[i]].Add(fighters1[i]);
                fighter2[yearNormed[i]].Add(fighters2[i]);
                outcomes[yearNormed[i]].Add(outcome[i]);
                winTypes[yearNormed[i]].Add(wintype[i]);
            }


            fighter1Data = Util.ArrayInit(nYears, year => fighter1[year].ToArray());
            fighter2Data = Util.ArrayInit(nYears, year => fighter2[year].ToArray());
            outcomeData = Util.ArrayInit(nYears, year => outcomes[year].ToArray());
            winTypeData = Util.ArrayInit(nYears, year => winTypes[year].ToArray());

            firstYearData = new int[nFighters];
            lastYearData = new int[nFighters];
            for (int i = 0; i < nFighters; i++)
            {
                firstYearData[i] = fighterYears[i].Min() - minYear;
                lastYearData[i] = fighterYears[i].Max() - minYear;
            }


        }

        public void Run()
        {
            InferenceEngine engine = new InferenceEngine();
            if (!(engine.Algorithm is ExpectationPropagation))
            {
                return;
            }

            int[][] fighter1Data, fighter2Data, outcomeData, winTypeData;
            int[] firstYearData;
            int[] lastYearData;

            int nFighters, nYears, startYear;
            LoadData(out fighter1Data, out fighter2Data, out outcomeData, out winTypeData, out firstYearData, out lastYearData,
                out nFighters, out nYears, out startYear);

            //Skill prior
            var skillPrior = new Gaussian(1000, 500 * 500);
            var performancePrecisionPrior = Gamma.FromShapeAndRate(2, 26 * 26);
            var skillChangePrecisionPrior = Gamma.FromShapeAndRate(2, 26 * 26);

            var performancePrecision = Variable.Random(performancePrecisionPrior).Named("performancePrecision");
            var skillChangePrecision = Variable.Random(skillChangePrecisionPrior).Named("skillChangePrecision");

            var matchupThresholdPrior = new Gaussian(200, 50 * 50);
            var matchupThreshold = Variable.Random(matchupThresholdPrior).Named("matchupThreshold");

            var finishThresholdPrior = new Gaussian(20, 10 * 10);
            var finishThreshold = Variable.Random(finishThresholdPrior).Named("finishThreshold");

            var decicionThresholdPrior = new Gaussian(10, 10 * 10);
            var decisionThreshold = Variable.Random(decicionThresholdPrior).Named("decisionThreshold");

            Range fighter = new Range(nFighters).Named("fighter");
            Range year = new Range(nYears).Named("year");
            VariableArray<int> firstYear = Variable.Array<int>(fighter).Named("firstYear");
            var skill = Variable.Array(Variable.Array<double>(fighter), year).Named("skill");


            using (var yearBlock = Variable.ForEach(year))
            {
                var y = yearBlock.Index;
                using (Variable.If(y == 0))
                {
                    skill[year][fighter] = Variable.Random(skillPrior).ForEach(fighter);
                }
                using (Variable.If(y > 0))
                {
                    using (Variable.ForEach(fighter))
                    {
                        Variable<bool> isFirstYear = (firstYear[fighter] >= y).Named("isFirstYear");
                        using (Variable.If(isFirstYear))
                        {
                            skill[year][fighter] = Variable.Random(skillPrior);
                        }
                        using (Variable.IfNot(isFirstYear))
                        {
                            skill[year][fighter] = Variable.GaussianFromMeanAndPrecision(skill[y - 1][fighter], skillChangePrecision);
                        }
                    }
                }
            }

            

            firstYear.ObservedValue = firstYearData;

            int[] nMatchesData = Util.ArrayInit(nYears, y => outcomeData[y].Length);
            var nMatches = Variable.Observed(nMatchesData, year).Named("nMatches");
            Range match = new Range(nMatches[year]).Named("match");
            var fighter1 = Variable.Observed(fighter1Data, year, match).Named("fighter1");
            var fighter2 = Variable.Observed(fighter2Data, year, match).Named("fighter2");
            var outcome = Variable.Observed(outcomeData, year, match).Named("outcome");
            var winType = Variable.Observed(winTypeData, year, match).Named("winType");

            Variable.ConstrainTrue(finishThreshold > decisionThreshold);
            Variable.ConstrainTrue(decisionThreshold > 0);


            using (Variable.ForEach(year))
            {
                using (Variable.ForEach(match))
                {
                    var w = fighter1[year][match];
                    var b = fighter2[year][match];
                    Variable<double> fighter1_performance = Variable.GaussianFromMeanAndPrecision(skill[year][w], performancePrecision).Named("fighter1_performance");
                    Variable<double> fighter2_performance = Variable.GaussianFromMeanAndPrecision(skill[year][b], performancePrecision).Named("fighter2_performance");

                    Variable.ConstrainFalse(skill[year][w] - skill[year][b] > matchupThreshold);
                    Variable.ConstrainFalse(skill[year][b] - skill[year][w] > matchupThreshold);

                    using (Variable.Case(outcome[year][match], 0))
                    { // fighter2 wins
                        Variable.ConstrainTrue( (fighter2_performance - fighter1_performance)  > decisionThreshold);

                        //Finish
                        using (Variable.Case(winType[year][match], 1))
                        {
                            Variable.ConstrainTrue((fighter2_performance - fighter1_performance)  > finishThreshold);
                        }

                    }
                    using (Variable.Case(outcome[year][match], 1))
                    { // draw

                        Variable.ConstrainFalse((fighter2_performance - fighter1_performance) < decisionThreshold);
                        Variable.ConstrainFalse((fighter1_performance - fighter2_performance) < decisionThreshold);
                    }
                    using (Variable.Case(outcome[year][match], 2))
                    { // fighter1 wins
                        Variable.ConstrainTrue((fighter1_performance - fighter2_performance) > decisionThreshold);
                        //Finish
                        using (Variable.Case(winType[year][match], 1))
                        {
                           Variable.ConstrainTrue((fighter1_performance - fighter2_performance) > finishThreshold);
                        }
                    }
                }
            }
            year.AddAttribute(new Sequential());   // helps inference converge faster
            engine.Compiler.UseSerialSchedules = false;
            engine.NumberOfIterations = 200;
            var skillPost = engine.Infer<Gaussian[][]>(skill);
            var matchupThresholdPost = engine.Infer<Gaussian>(matchupThreshold);
            var decisionThresholdPost = engine.Infer<Gaussian>(decisionThreshold);
            var finishThresholdPost = engine.Infer<Gaussian>(finishThreshold);
            var skillChangePrecisionPost = engine.Infer<Gamma>(skillChangePrecision);

            Console.WriteLine("Matchup Threshold prec {0}", matchupThresholdPost);
            Console.WriteLine("Decision Threshold {0}", decisionThresholdPost); 
            Console.WriteLine("Finish threshold {0}", finishThresholdPost); 
            Console.WriteLine("Skilll change prec {0}", skillChangePrecisionPost);

            using (System.IO.StreamWriter file =
                new System.IO.StreamWriter(@"outfile.txt"))
            {
                file.WriteLine("key year name skillmean variance");
                for (int i = 0; i < nFighters; i++)
                {
                    for (int y = 0; y < nYears; y++)
                    {
                        if (y >= firstYearData[i] && y <= lastYearData[i])
                        {
                            file.WriteLine("{0} {1} {2} {3} {4}", i, startYear + y, keyToFighter[i], skillPost[y][i].GetMean(), skillPost[y][i].GetVariance());
                        }
                    }
                }
            }
        }

        Dictionary<int, string> keyToFighter = new Dictionary<int, string>();
        Dictionary<string, int> fighterToKey = new Dictionary<string, int>();
        
        
    }
}
