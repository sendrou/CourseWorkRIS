using MPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

public class ServerApp
{
    private byte[] referenceImage;
    private List<byte[]> comparisonImages = new List<byte[]>();
    static Communicator communicator;
    private static bool delivery = false;
    static string flagMirror;
    static ConcurrentDictionary<int, bool> busyClusters; // Потокобезопасная коллекция
    static ConcurrentBag<int> clusterNumbers = new ConcurrentBag<int>();
    static int localPort = 5000; // Порт для приема сообщений
    static Socket listeningSocket;
    static string ServerAddress = "127.0.0.1"; // IP-адрес сервера
    private static readonly object socketLock = new object();
    private static readonly object communicatorLock = new object(); // Блокировка для синхронизации доступа к communicator

    public static void Main(string[] args)
    {
        using (new MPI.Environment(ref args))
        {
            var comm = Communicator.world;
            var server = new ServerApp();
            if (comm.Rank == 0)
            {
                server.Start(comm); // Запуск сервера, если ранг 0
            }
        }
    }

    public ServerApp()
    {
        try
        {
            communicator = Communicator.world;
            Start(communicator);
            listeningSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            Task listeningTask = Task.Run(Listen); // Используем Task.Run для запуска задачи
            Console.WriteLine("UDP Server start");
            Console.ReadLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    static void Listen()
    {
        IPEndPoint localIP = new IPEndPoint(IPAddress.Parse(ServerAddress), localPort);
        listeningSocket.Bind(localIP);

        ConcurrentDictionary<int, ClientData> clientData = new ConcurrentDictionary<int, ClientData>();

        while (true)
        {
            try
            {
                byte[] data = new byte[65000]; // Максимальный размер UDP-пакета
                EndPoint remoteIp = new IPEndPoint(IPAddress.Any, 0);

                int bytes = listeningSocket.ReceiveFrom(data, ref remoteIp);
                IPEndPoint clientEndPoint = remoteIp as IPEndPoint;
                int clientPort = clientEndPoint.Port;

                ClientData currentClient = clientData.GetOrAdd(clientPort, _ => new ClientData());

                if (Encoding.Unicode.GetString(data, 0, bytes) == "done")
                {
                    Console.WriteLine($"Client {clientPort} end send.");
                    Console.WriteLine($"Images from client {clientPort}: {currentClient.ComparisonImages.Count}");

                    // Проверяем, получены ли данные полностью
                    if (currentClient.ReferenceImage == null ||
                        currentClient.ComparisonImages.Count < currentClient.ExpectedImageCount)
                    {
                        SendImageIndexBackToClient(-2, remoteIp);
                        Console.WriteLine($"Client {clientPort}: incomplete data received, sending error code -2.");
                        currentClient.Clear();
                        continue;
                    }

                    Task.Run(() =>
                    {
                        Stopwatch stopwatch = new Stopwatch();
                        stopwatch.Start();

                        currentClient.MirroredImages = SendDataToFreeCluster(
                            currentClient.ReferenceImage,
                            currentClient.ComparisonImages
                        );

                        int mirrorIndex = -1;
                        for (int i = 0; i < currentClient.ComparisonImages.Count; i++)
                        {
                            if (currentClient.MirroredImages[0].SequenceEqual(currentClient.ComparisonImages[i]))
                                mirrorIndex = i;
                        }

                        SendImageIndexBackToClient(mirrorIndex, remoteIp);
                        stopwatch.Stop();
                        Console.WriteLine($"Time taken for client {clientPort}: {stopwatch.Elapsed}");
                        currentClient.Clear();
                    });
                }
                else
                {
                    // Извлекаем метаданные из пакета
                    int partNumber = BitConverter.ToInt32(data, 0);
                    int totalPackets = BitConverter.ToInt32(data, 4);
                    int totalImages = BitConverter.ToInt32(data, 8); // Количество изображений, переданных клиентом

                    byte[] partData = new byte[bytes - 12];
                    Array.Copy(data, 12, partData, 0, partData.Length);

                    // Сохраняем общее количество изображений
                    if (currentClient.ExpectedImageCount == 0)
                    {
                        currentClient.ExpectedImageCount = totalImages;
                    }

                    // Собираем данные пакетов
                    if (currentClient.ReceivedParts.TryAdd(partNumber, partData) &&
                        currentClient.ReceivedParts.Count == totalPackets)
                    {
                        try
                        {
                            // Собираем полное изображение
                            byte[] fullImage = new byte[currentClient.ReceivedParts.Values.Sum(part => part.Length)];
                            int offset = 0;

                            foreach (var part in currentClient.ReceivedParts.OrderBy(kv => kv.Key).Select(kv => kv.Value))
                            {
                                Array.Copy(part, 0, fullImage, offset, part.Length);
                                offset += part.Length;
                            }

                            // Сохраняем изображение как эталонное или сравниваемое
                            if (currentClient.ReferenceImage == null)
                                currentClient.ReferenceImage = fullImage;
                            else
                                currentClient.ComparisonImages.Add(fullImage);

                            Console.WriteLine($"Client {clientPort}: image size : {fullImage.Length} received.");
                            currentClient.ReceivedParts.Clear();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error assembling image for client {clientPort}: {ex.Message}");

                            // Сбрасываем данные клиента и отправляем код ошибки (-2)
                            currentClient.Clear();
                            SendImageIndexBackToClient(-2, remoteIp);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }


    class ClientData
    {
        public ConcurrentDictionary<int, byte[]> ReceivedParts { get; } = new ConcurrentDictionary<int, byte[]>();
        public byte[] ReferenceImage { get; set; }
        public List<byte[]> ComparisonImages { get; } = new List<byte[]>();
        public List<byte[]> MirroredImages { get; set; }
        public int ExpectedImageCount { get; set; } = 0; // Ожидаемое количество изображений

        public void Clear()
        {
            ReceivedParts.Clear();
            ComparisonImages.Clear();
            MirroredImages?.Clear();
            ReferenceImage = null;
            ExpectedImageCount = 0;

        }
    }

    private static void SendImageIndexBackToClient(int imageIndex, EndPoint clientEndpoint)
    {
        try
        {
            byte[] packet = BitConverter.GetBytes(imageIndex);
            lock (socketLock)
            {
                listeningSocket.SendTo(packet, clientEndpoint);
            }
            Console.WriteLine($"Sent image index: {imageIndex} to client.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending image index: {ex.Message}");
        }
    }

    public void Start(Communicator comm)
    {
        communicator = comm;
        string configFilePath = "D:\\riscourse\\RIS\\StartMPI\\bin\\Debug\\config.json";

        if (File.Exists(configFilePath))
        {
            try
            {
                string json = File.ReadAllText(configFilePath);
                dynamic config = JsonConvert.DeserializeObject(json);

                if (config.cluster != null)
                {
                    foreach (var rank in config.cluster)
                    {
                        clusterNumbers.Add((int)rank);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading config.json: {ex.Message}");
            }
        }

        busyClusters = new ConcurrentDictionary<int, bool>();
        foreach (var cluster in clusterNumbers)
        {
            busyClusters.TryAdd(cluster, false);
        }
    }

  

    private static readonly ConcurrentDictionary<int, int> clusterUsageCount = new ConcurrentDictionary<int, int>();
    private static readonly object fileLock = new object(); // Для синхронизации записи в файл
    private static readonly Random random = new Random();

    private static List<byte[]> SendDataToFreeCluster(byte[] referenceImage, List<byte[]> comparisonImages)
    {
        while (true)
        {
            int freeCluster = 0;

            lock (busyClusters)
            {
                var freeClusters = busyClusters
                    .Where(kvp => !kvp.Value) // Находим все свободные кластеры
                    .Select(kvp => kvp.Key)
                    .ToList();

                if (freeClusters.Any())
                {
                    freeCluster = freeClusters[random.Next(freeClusters.Count)]; // Случайный свободный кластер
                }
            }

            try
            {
                if (freeCluster != 0)
                {
                    Console.WriteLine($"Cluster {freeCluster}: {busyClusters[freeCluster]} -> true");

                    lock (busyClusters)
                    {
                        busyClusters[freeCluster] = true;
                    }

                    // Увеличиваем счётчик использования кластера
                    clusterUsageCount.AddOrUpdate(freeCluster, 1, (key, oldValue) => oldValue + 1);

                    // Записываем общее количество использований в файл
                    LogClusterUsageCounts();

                    Console.WriteLine($"Sending data to cluster {freeCluster}");

                    List<byte[]> mirroredImages;

                    // Синхронизируем доступ к communicator
                    lock (communicatorLock)
                    {
                        // Отправляем данные эталонного изображения и список изображений для сравнения
                        communicator.Send(new List<byte[]>(1) { referenceImage }, freeCluster, 0);
                        communicator.Send(comparisonImages, freeCluster, 1);
                    }
                    lock (communicatorLock)
                    {
                        // Получаем ответ от кластера
                        flagMirror = communicator.Receive<string>(freeCluster, 300);

                        // Получаем результат
                        mirroredImages = flagMirror == "yes"
                            ? communicator.Receive<List<byte[]>>(freeCluster, 400)
                            : new List<byte[]> { File.ReadAllBytes(@"D:\riscourse\RIS\StartMPI\bin\Debug\no.png") };
                    }

                    Console.WriteLine($"Cluster {freeCluster}: {busyClusters[freeCluster]} -> false");
                    return mirroredImages;
                }

                // Если все кластеры заняты, ждем и повторяем попытку
                Thread.Sleep(1000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обработке кластера {freeCluster}: {ex.Message}");
            }
            finally
            {
                if (freeCluster != 0)
                {
                    lock (busyClusters)
                    {
                        busyClusters[freeCluster] = false;
                    }
                }
            }
        }
    }

    private static void LogClusterUsageCounts()
    {
        lock (fileLock)
        {
            try
            {
                string logPath = "cluster_usage_counts.txt";

                // Генерируем текст для записи
                var logContent = clusterUsageCount
                    .OrderBy(kvp => kvp.Key) // Сортируем по ключу (номеру кластера)
                    .Select(kvp => $"Cluster {kvp.Key}: {kvp.Value} usages")
                    .ToArray();

                // Записываем всё содержимое в файл
                File.WriteAllLines(logPath, logContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing to usage count file: {ex.Message}");
            }
        }
    }

}
