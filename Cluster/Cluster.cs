using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using MPI;

public class ClusterApp
{
    public static void Main(string[] args)
    {
        using (new MPI.Environment(ref args))
        {
            var comm = Communicator.world;
            var cluster = new ClusterApp();

            
                cluster.Start(comm);

        }
    }

    public void Start(Communicator comm)
    {
        int rank = comm.Rank;

        // Только кластерные ранги запускают логику
        Console.WriteLine($"Cluster {rank}: Initializing...");

        // Путь к файлу конфигурации
        string configFilePath = "D:\\riscourse\\RIS\\StartMPI\\bin\\Debug\\config.json";

        // Считывание и парсинг данных конфигурации
        var configData = ReadConfig(configFilePath);
        if (configData == null || configData.ClusterUnitCount <= 0)
        {
            Console.WriteLine($"Cluster {rank}: Invalid configuration. Exiting...");
            return;
        }

        // Генерация кластерных юнитов для текущего ранга
        int[] clusterUnits = GenerateClusterUnits(rank, configData, comm.Size);

        Console.WriteLine($"Cluster {rank}: Detected units: {string.Join(", ", clusterUnits)}");

        while (true) // Основной цикл ожидания задач
        {
            Console.WriteLine($"Cluster {rank}: Waiting for tasks...");

            // Получение эталонного изображения
            List<byte[]> referenceImage = comm.Receive<List<byte[]>>(source: 0, tag: 0);
            Console.WriteLine($"Cluster {rank}: Reference image received successfully.");

            // Получение изображений для сравнения
            List<byte[]> comparisonImages = comm.Receive<List<byte[]>>(source: 0, tag: 1);
            Console.WriteLine($"Cluster {rank}: Comparison images received successfully. Count = {comparisonImages.Count}");

            // Распределение изображений по узлам
            List<byte[]>[] distributedImages = new List<byte[]>[clusterUnits.Length];
            for (int i = 0; i < clusterUnits.Length; i++)
            {
                distributedImages[i] = new List<byte[]>();
            }

            for (int i = 0; i < comparisonImages.Count; i++)
            {
                int clusterIndex = i % clusterUnits.Length;
                distributedImages[clusterIndex].Add(comparisonImages[i]);
            }
            int clusterSendCount = 0;
            // Отправка данных юнитам
            for (int i = 0; i < clusterUnits.Length; i++)
            {
                int clusterUnitNumber = clusterUnits[i];
                if (distributedImages[i].Count > 0)
                {
                    clusterSendCount++;
                    comm.Send(referenceImage, clusterUnitNumber, 100); // Отправляем эталонное изображение
                    comm.Send(distributedImages[i], clusterUnitNumber, 101); // Отправляем изображения для обработки
                }
            }

            
            // Сбор результатов от узлов
            List<byte[]> clusterResult = new List<byte[]>();
            int clusterNoMirrorCount=0;
            int clusterAnswerCount=0;
            for (int i = 0; i < clusterUnits.Length; i++)
            {
                int clusterUnitNumber = clusterUnits[i];

                // Принятие сообщений от кластера
                string resultFlag = comm.Receive<string>(source: clusterUnitNumber, tag: 350);

                if (resultFlag == "yes")
                {
                    // Если "yes", получаем результаты (изображения)
                    clusterResult = comm.Receive<List<byte[]>>(source: clusterUnitNumber, tag: 401);
                    clusterAnswerCount++;
                    
                }
                else
                {
                    // Если "no", сообщений нет
                    Console.WriteLine($"Cluster {rank}: No results received.");
                    clusterNoMirrorCount++;
                    clusterAnswerCount++;
                    if (clusterNoMirrorCount == clusterSendCount)
                    {
                        comm.Send("no", 0, 300);

                        break;
                    }
                    
                }
                if (clusterAnswerCount == clusterSendCount) break;
            }

            if (clusterResult.Count > 0)
            {
                comm.Send("yes", 0, 300);
                Console.WriteLine($"Cluster {rank}: Received {clusterResult.Count} images.");
                comm.Send(clusterResult, 0, 400);
            }


            Console.WriteLine($"Cluster {rank}: Results sent to server.");
            referenceImage = null;
            comparisonImages = null;
            clusterNoMirrorCount = 0;
            clusterSendCount= 0;
        }
    }

    private ConfigData ReadConfig(string configFilePath)
    {
        try
        {
            if (!File.Exists(configFilePath))
            {
                Console.WriteLine($"Configuration file not found at {configFilePath}");
                return null;
            }

            // Читаем и парсим JSON
            string jsonContent = File.ReadAllText(configFilePath);
            var jsonObject = JObject.Parse(jsonContent);

            // Извлекаем данные
            int clusterUnitCount = jsonObject["clusterUnitCount"]?.ToObject<int>() ?? 0;

            return new ConfigData
            {
                ClusterUnitCount = clusterUnitCount
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading configuration: {ex.Message}");
            return null;
        }
    }

    private int[] GenerateClusterUnits(int rank, ConfigData configData, int commSize)
    {
        // Формируем список кластерных юнитов на основе ранга текущего кластера
        List<int> clusterUnits = new List<int>();
        for (int i = 1; i <= configData.ClusterUnitCount; i++)
        {
            int clusterUnit = rank + i;
            if (clusterUnit < commSize) // Учитываем общее число процессов
            {
                clusterUnits.Add(clusterUnit);
            }
        }

        return clusterUnits.ToArray();
    }
}

// Вспомогательный класс для хранения данных конфигурации
public class ConfigData
{
    public int ClusterUnitCount { get; set; }
}
