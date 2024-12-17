using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;
using System.Drawing;

namespace Client
{
    public partial class Client : Form
    {
        private Button btnUploadReference;
        private Button btnUploadComparison;
        private Button btnCompareImages;
        private Label lblReferenceStatus;
        private ListBox lstComparisonImages;
        private Label lblResult;
        static private PictureBox mirrorPictureBox;
        private PictureBox normalPictureBox;
        private Label lblNormalImage;
        private Label lblMirroredImage;

        private byte[] referenceImage;
        private List<byte[]> comparisonImages = new List<byte[]>();


        static int remotePort = 5000;
        public static Socket listeningSocket;
        static string ServerAddress = "127.0.0.1";
        static int maxPacketSize = 65000 - 12; // Максимальный размер данных пакета (оставляем место для номера пакета и общего числа пакетов)

        public Client()
        {
            // Создаем сокет для UDP
            listeningSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            // Указываем IP-адрес и порт сервера
            IPEndPoint ipPoint = new IPEndPoint(IPAddress.Parse(ServerAddress), remotePort);
            listeningSocket.Connect(ipPoint);







            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            this.Text = "Image Comparison Client";
            this.ClientSize = new System.Drawing.Size(1000, 400);

            lblNormalImage = new Label
            {
                Text = "Normal Image",
                Location = new System.Drawing.Point(450, 50),
                Width = 200,
                TextAlign = ContentAlignment.MiddleCenter
            };

            lblMirroredImage = new Label
            {
                Text = "Mirrored Image",
                Location = new System.Drawing.Point(700, 50),
                Width = 200,
                TextAlign = ContentAlignment.MiddleCenter
            };

            normalPictureBox = new PictureBox
            {
                Location = new System.Drawing.Point(450, 80),
                Size = new System.Drawing.Size(200, 200),
                SizeMode = PictureBoxSizeMode.Zoom
            };

            mirrorPictureBox = new PictureBox
            {
                Location = new System.Drawing.Point(700, 80),
                Size = new System.Drawing.Size(200, 200),
                SizeMode = PictureBoxSizeMode.Zoom
            };

            btnUploadReference = new Button
            {
                Text = "Upload Reference Image",
                Location = new System.Drawing.Point(20, 20),
                Width = 250
            };
            btnUploadReference.Click += BtnUploadReference_Click;

            lblReferenceStatus = new Label
            {
                Text = "Reference image not uploaded",
                Location = new System.Drawing.Point(20, 60),
                Width = 300
            };

            btnUploadComparison = new Button
            {
                Text = "Upload Comparison Images",
                Location = new System.Drawing.Point(20, 100),
                Width = 250
            };
            btnUploadComparison.Click += BtnUploadComparison_Click;

            lstComparisonImages = new ListBox
            {
                Location = new System.Drawing.Point(20, 140),
                Width = 250,
                Height = 100
            };

            btnCompareImages = new Button
            {
                Text = "Compare Images",
                Location = new System.Drawing.Point(20, 260),
                Width = 250
            };
            btnCompareImages.Click += BtnCompareImages_Click;

            lblResult = new Label
            {
                Text = "Result will be displayed here",
                Location = new System.Drawing.Point(20, 300),
                Width = 500
            };

            this.Controls.Add(btnUploadReference);
            this.Controls.Add(lblReferenceStatus);
            this.Controls.Add(btnUploadComparison);
            this.Controls.Add(lstComparisonImages);
            this.Controls.Add(btnCompareImages);
            this.Controls.Add(lblResult);
            this.Controls.Add(lblNormalImage);
            this.Controls.Add(normalPictureBox);
            this.Controls.Add(lblMirroredImage);
            this.Controls.Add(mirrorPictureBox);

            this.ResumeLayout(false);
        }
        public void Listen()
        {
            while (true)
            {
                int bytes = 0; // количество полученных байтов
                byte[] data = new byte[256]; // буфер для получаемых данных

                // адрес, с которого пришли данные
                EndPoint remoteIp = new IPEndPoint(IPAddress.Any, 0);

                do
                {
                    bytes = listeningSocket.ReceiveFrom(data, ref remoteIp);
                }
                while (listeningSocket.Available > 0);

                // получаем данные о подключении
                IPEndPoint remoteFullIp = remoteIp as IPEndPoint;

                // Конвертируем первые 4 байта в целое число
                if (bytes >= 4)
                {
                    int receivedInt = BitConverter.ToInt32(data, 0);
                    Console.WriteLine($"{Client.listeningSocket.LocalEndPoint}");
                    // Передаем это число в функцию
                    DisplayImageInPictureBox(receivedInt, comparisonImages);
                }
            }
        }





        // Этот метод вызывается после того, как изображение собрано
        private  void DisplayImageInPictureBox(int imageIndex, List<byte[]> comparisonImages)
        {
            mirrorPictureBox.Image = null;
            if(imageIndex != -1){
                using (MemoryStream ms = new MemoryStream(comparisonImages[imageIndex]))
                {
                    // Создаем изображение из байтов
                    Image image = Image.FromStream(ms);

                    // Предположим, что у вас есть PictureBox на форме, например, mirrorPictureBox
                    mirrorPictureBox.Image = image;
                }
            }
            else
            {
                string noImagePath = "no.png"; // Убедитесь, что путь к файлу верный
                if (File.Exists(noImagePath))
                {
                    mirrorPictureBox.Image = Image.FromFile(noImagePath);
                }

            }
                
            
            
        }

        private static void Close()
        {
            if (listeningSocket != null)
            {
                listeningSocket.Shutdown(SocketShutdown.Both);
                listeningSocket.Close();
                listeningSocket = null;
            }
        }


       
        private void BtnUploadReference_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp";
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    referenceImage = File.ReadAllBytes(openFileDialog.FileName);
                    lblReferenceStatus.Text = "Reference image uploaded";
                    normalPictureBox.Image = Image.FromStream(new MemoryStream(referenceImage));
                }
            }
        }

        private void BtnUploadComparison_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp";
                openFileDialog.Multiselect = true;
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    foreach (string fileName in openFileDialog.FileNames)
                    {
                        byte[] imageData = File.ReadAllBytes(fileName);
                        comparisonImages.Add(imageData);
                        lstComparisonImages.Items.Add(Path.GetFileName(fileName));
                    }
                }
            }
        }



        private  void BtnCompareImages_Click(object sender, EventArgs e)
        {
            if (referenceImage == null || comparisonImages.Count == 0)
            {
                MessageBox.Show("Upload a reference image and at least one comparison image.");
                return;
            }
               
                //// Отправка эталонного изображения
                SendImages(referenceImage,comparisonImages.Count);

                // Отправка изображений для сравнения
                foreach (var image in comparisonImages)
                {
                    SendImages(image, comparisonImages.Count);
                }
                byte[] doneMessage = Encoding.Unicode.GetBytes("done");
                listeningSocket.Send(doneMessage);
                // Запускаем поток для прослушивания
                Task listeningTask = new Task(Listen);
                listeningTask.Start();
            
        }

       public void SendImages(byte[] imageData, int totalImages)
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
                listeningSocket.Send(packet);
                Console.WriteLine($"Отправлен пакет {i + 1}/{totalPackets}, размер пакета: {size + 12} байт");
                Thread.Sleep(5);
            }
            Thread.Sleep(20);
            Console.WriteLine("Изображение отправлено.");
        }
    }

}
