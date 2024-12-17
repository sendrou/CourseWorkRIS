using MPI;
using System;
using System.IO;
using Newtonsoft.Json;
using System.Collections.Generic;

public class StartMPI
{
    public static void Main(string[] args)
    {
        Dictionary<string, List<int>> ranksMap = new Dictionary<string, List<int>>();
        string configFilePath = "D:\\riscourse\\RIS\\StartMPI\\bin\\Debug\\config.json"; // Укажите правильный путь к файлу config.json
        if (File.Exists(configFilePath))
        {
            string json = File.ReadAllText(configFilePath);
            dynamic config = JsonConvert.DeserializeObject(json);

            foreach (var item in config)
            {
                string groupName;
                if (item.Name == "clusterUnitCount")
                {
                    groupName = "clusterUnit";
                    List<int> ranks = new List<int>(); ;
                    for (int x = 0; x < ranksMap["cluster"].Count; x++)
                    {
                        
                        for (int i = 1; i < Convert.ToInt32(item.Value) + 1; i++)
                        {
                            ranks.Add((int)ranksMap["cluster"][x] + i);
                        }
                    }
                    ranksMap[groupName] = ranks;
                }
                else
                {
                    groupName = item.Name;
                    List<int> ranks = new List<int>();

                    foreach (var rank in item.Value)
                    {
                        ranks.Add((int)rank);
                    }
                    ranksMap[groupName] = ranks;
                }



            }
        }
        else
        {
            Console.WriteLine("Файл config.json не найден.");
        }
        

        using (new MPI.Environment(ref args))
        {
            Intracommunicator communicator = Communicator.world;

            foreach (var group in ranksMap)
            {
                if (group.Value.Contains(communicator.Rank))
                {
                    Console.WriteLine($"{group.Key} Process {communicator.Rank} started");
                    // Здесь вы можете запустить соответствующую логику для каждой группы
                    switch (group.Key)
                    {
                        case "server":
                            var server = new ServerApp();
                            server.Start(communicator);
                            break;
                        case "cluster":
                            var cluster = new ClusterApp();
                            cluster.Start(communicator);
                            break;
                        case "clusterUnit":

                            var clusterUnit = new ClusterUnitApp();
                            clusterUnit.Start(communicator);
                            break;
                        // Добавьте другие группы по мере необходимости
                        default:
                            Console.WriteLine($"Unknown group for rank {communicator.Rank}");
                            break;
                    }
                }
            }
        }
    }
}