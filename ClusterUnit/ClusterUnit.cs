using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using MPI;
using Newtonsoft.Json;

public class ClusterUnitApp
{
    public static void Main(string[] args)
    {
        using (new MPI.Environment(ref args))
        {
            var comm = Communicator.world;
            var clusterUnit = new ClusterUnitApp();

            
                clusterUnit.Start(comm);
            
        }
    }

    public void Start(Communicator comm)
    {
        int rank = comm.Rank;

        Console.WriteLine($"ClusterUnit {rank}: Initializing...");

        // Загрузка конфигурации
        string configFilePath = "D:\\riscourse\\RIS\\StartMPI\\bin\\Debug\\config.json";
        Config config = LoadConfig(configFilePath);

        // Определение кластера для текущего ранга
        int clusterRank = DetermineClusterRank(config, rank);

        while (true) // Основной цикл обработки задач
        {
            
                // Получение эталонного изображения
                List<byte[]> referenceImage = comm.Receive<List<byte[]>>(source: clusterRank, tag: 100);
                Console.WriteLine($"ClusterUnit {rank}: Reference image received successfully.");

                // Получение изображений для сравнения
                List<byte[]> comparisonImages = comm.Receive<List<byte[]>>(source: clusterRank, tag: 101);
                Console.WriteLine($"ClusterUnit {rank}: Comparison images received successfully. Count = {comparisonImages.Count}");

                // Обработка изображений
                List<byte[]> clusterResult = ProcessImages(referenceImage[0], comparisonImages);

                // Отправка результата обратно в кластер
                if (clusterResult.Count > 0)
                {
                    comm.Send("yes", clusterRank, 350);
                    comm.Send(clusterResult, clusterRank, 401);
                }  
                else    
                    comm.Send("no", clusterRank, 350);
                Console.WriteLine($"ClusterUnit {rank}: Results sent back to cluster {clusterRank}.");

                referenceImage.Clear();
                comparisonImages.Clear();
            
        }
    }

    private List<byte[]> ProcessImages(byte[] referenceImageData, List<byte[]> comparisonImages)
    {
        List<byte[]> mirroredResults = new List<byte[]>();

        Bitmap referenceImage = ConvertBytesToBitmap(referenceImageData);

        for (int i = 0; i < comparisonImages.Count; i++)
        {
            Bitmap comparisonImage = ConvertBytesToBitmap(comparisonImages[i]);

            // Проверка зеркальности
            bool isMirrored = AreImagesMirrored(referenceImage, comparisonImage);
            if (isMirrored)
            {
                Console.WriteLine($"ClusterUnit {Communicator.world.Rank}: Image {i} is mirrored.");
                mirroredResults.Add(comparisonImages[i]); // Добавляем совпавшее изображение в результат
            }
        }

        return mirroredResults;
    }

    public Bitmap ConvertBytesToBitmap(byte[] imageData)
    {
        using (MemoryStream ms = new MemoryStream(imageData))
        {
            return new Bitmap(ms);
        }
    }

    public bool AreImagesMirrored(Bitmap referenceImage, Bitmap comparisonImage)
    {
        try
        {
            Bitmap mirroredImage = MirrorImage(comparisonImage);
            return CompareImagesWithTolerance(referenceImage, mirroredImage);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during image comparison: {ex.Message}");
            return false;
        }
    }

    public Bitmap MirrorImage(Bitmap image)
    {
        Bitmap mirroredImage = new Bitmap(image);
        mirroredImage.RotateFlip(RotateFlipType.RotateNoneFlipX); // Отражение по горизонтали
        return mirroredImage;
    }

    public bool CompareImagesWithTolerance(Bitmap image1, Bitmap image2, int tolerance = 60)
    {
        if (image1.Width != image2.Width || image1.Height != image2.Height)
            return false;

        BitmapData data1 = image1.LockBits(new Rectangle(0, 0, image1.Width, image1.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        BitmapData data2 = image2.LockBits(new Rectangle(0, 0, image2.Width, image2.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

        try
        {
            unsafe
            {
                byte* ptr1 = (byte*)data1.Scan0;
                byte* ptr2 = (byte*)data2.Scan0;

                for (int y = 0; y < data1.Height; y++)
                {
                    for (int x = 0; x < data1.Width * 3; x++) // 3 байта на пиксель
                    {
                        int offset = y * data1.Stride + x;
                        if (Math.Abs(ptr1[offset] - ptr2[offset]) > tolerance)
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }
        finally
        {
            image1.UnlockBits(data1);
            image2.UnlockBits(data2);
        }
    }

    public Config LoadConfig(string path)
    {
        string json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<Config>(json); 
    }

    public int DetermineClusterRank(Config config, int rank)
    {
        // Проверяем, в каком кластере находится текущий юнит
        foreach (var clusterRank in config.Cluster)
        {
            if (rank > clusterRank && rank <= clusterRank + config.ClusterUnitCount)
            {
                return clusterRank; // Возвращаем ранг кластера, которому принадлежит юнит
            }
        }

        throw new Exception($"ClusterUnit {rank}: Unable to determine cluster rank.");
    }
}

public class Config
{
    public List<int> Server { get; set; }
    public List<int> Cluster { get; set; }
    public int ClusterUnitCount { get; set; }
}
