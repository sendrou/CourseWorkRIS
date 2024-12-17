using Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClientSpawner
{
    class Client
    {
        private static readonly object consoleLock = new object(); // Для потокобезопасного логирования
        private static string ServerAddress = "127.0.0.1"; // Адрес сервера
        private static int remotePort = 5000; // Порт сервера
        private static int maxPacketSize = 65000 - 12; // Максимальный размер данных пакета
        private Socket clientSocket; // Сокет клиента
        private IPEndPoint serverEndpoint; // Адрес сервера

        public Client()
        {
            // Создаём сокет для UDP
            clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            serverEndpoint = new IPEndPoint(IPAddress.Parse(ServerAddress), remotePort);

            //lock (consoleLock)
            //{
            //    Console.WriteLine("Сокет клиента успешно создан.");
            //}
        }

        public void SendImage(byte[] referenceImage, List<byte[]> comparisonImages)
        {
            // Отправляем эталонное изображение
            SendImages(referenceImage, comparisonImages.Count);

            // Отправляем изображения для сравнения
            foreach (var image in comparisonImages)
            {
                SendImages(image, comparisonImages.Count);
            }

            // Отправляем сигнал о завершении
            byte[] doneMessage = Encoding.Unicode.GetBytes("done");
            clientSocket.SendTo(doneMessage, serverEndpoint);

            //lock (consoleLock)
            //{
            //    Console.WriteLine("Все изображения отправлены.");
            //}
        }

        private void SendImages(byte[] imageData, int totalImages)
        {
            int totalPackets = (int)Math.Ceiling((double)imageData.Length / maxPacketSize);

            // Отправка изображения в пакетах
            for (int i = 0; i < totalPackets; i++)
            {
                int size = Math.Min(maxPacketSize, imageData.Length - i * maxPacketSize);
                byte[] packet = new byte[size + 12]; // 12 байт: номер пакета, общее количество пакетов, общее количество изображений

                // Добавляем номер пакета
                BitConverter.GetBytes(i).CopyTo(packet, 0);

                // Добавляем общее количество пакетов
                BitConverter.GetBytes(totalPackets).CopyTo(packet, 4);

                // Добавляем общее количество изображений
                BitConverter.GetBytes(totalImages).CopyTo(packet, 8);

                // Копируем данные изображения
                Array.Copy(imageData, i * maxPacketSize, packet, 12, size);

                // Отправляем пакет
                clientSocket.SendTo(packet, serverEndpoint);



                Thread.Sleep(5); // Искусственная задержка для стабильности
            }
        }




        public static byte[] LoadRandomImageFromFolder(string folderPath)
        {
            var imageFiles = Directory.GetFiles(folderPath, "*.*")
                .Where(file => file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                               file.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                               file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                               file.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!imageFiles.Any())
            {
                throw new Exception("В папке нет подходящих изображений.");
            }

            Random random = new Random();
            string randomImagePath = imageFiles[random.Next(imageFiles.Count)];
            return File.ReadAllBytes(randomImagePath);
        }

        public static List<byte[]> LoadRandomImagesFromFolder(string folderPath, int count)
        {
            var imageFiles = Directory.GetFiles(folderPath, "*.*")
                .Where(file => file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                               file.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                               file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                               file.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (imageFiles.Count < count)
            {
                throw new Exception("Недостаточно изображений в папке.");
            }

            Random random = new Random();
            List<string> selectedImages = imageFiles.OrderBy(x => random.Next()).Take(count).ToList();

            return selectedImages.Select(File.ReadAllBytes).ToList();
        }

        public void Close()
        {
            if (clientSocket != null)
            {
                clientSocket.Shutdown(SocketShutdown.Both);
                clientSocket.Close();
                clientSocket = null;

                //lock (consoleLock)
                //{
                //    Console.WriteLine("Сокет клиента закрыт.");
                //}
            }
        }
        public void Listen(int clientId, string imageFolder, byte[] referenceImage, List<byte[]> comparisonImages)
        {
            byte[] buffer = new byte[256];
            EndPoint remoteEndpoint = new IPEndPoint(IPAddress.Any, 0);

            Stopwatch stopwatch = Stopwatch.StartNew();  // Start measuring time

            while (true)
            {
                try
                {
                    int bytesReceived = clientSocket.ReceiveFrom(buffer, ref remoteEndpoint);

                    if (bytesReceived >= 4)
                    {
                        int receivedIndex = BitConverter.ToInt32(buffer, 0);

                        if (receivedIndex == -2)
                        {
                            Console.WriteLine($"Клиент {clientId} получил результат -2, повторная отправка данных...");
                            // Повторная отправка данных
                            SendImage(referenceImage, comparisonImages);
                        }
                        else
                        {
                            stopwatch.Stop();  // Stop measuring time when received anything other than -2
                            Console.WriteLine($"Клиент {clientId} принял результат: {receivedIndex}");
                            Console.WriteLine($"Время выполнения клиента {clientId}: {stopwatch.ElapsedMilliseconds} мс");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (consoleLock)
                    {
                        Console.WriteLine($"Ошибка при приёме данных клиентом {clientId}: {ex.Message}");
                    }
                    break;
                }
            }
        }

        class Program
        {
            static void Main(string[] args)
            {
                int clientCount = 1000; // Количество клиентов
                string imageFolder = @"D:\riscourse\img"; // Путь к папке с изображениями

                List<Task> clientTasks = new List<Task>();

                Console.WriteLine($"Запуск {clientCount} клиентов...");

                for (int i = 0; i < clientCount; i++)
                {
                    int clientId = i + 1;

                    clientTasks.Add(Task.Run(() =>
                    {
                        Client client = new Client();
                        Console.WriteLine($"{clientId} клиент запущен");

                        try
                        {
                            // Загрузка изображений
                            byte[] referenceImage = Client.LoadRandomImageFromFolder(imageFolder);
                            List<byte[]> comparisonImages = Client.LoadRandomImagesFromFolder(imageFolder, 10);

                            // Отправка изображений
                            client.SendImage(referenceImage, comparisonImages);

                            // Прослушивание ответов
                            client.Listen(clientId, imageFolder, referenceImage, comparisonImages);
                        }
                        finally
                        {
                            client.Close();
                        }
                    }));

                    Thread.Sleep(300); // Adding slight delay to simulate staggered client startup
                }

                Task.WaitAll(clientTasks.ToArray());
                Console.WriteLine("Все клиенты завершили работу.");
            }
        }
    }
}

        
