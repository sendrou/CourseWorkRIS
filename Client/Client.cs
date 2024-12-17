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
        static int maxPacketSize = 65000 - 12; // ������������ ������ ������ ������ (��������� ����� ��� ������ ������ � ������ ����� �������)

        public Client()
        {
            // ������� ����� ��� UDP
            listeningSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            // ��������� IP-����� � ���� �������
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
                int bytes = 0; // ���������� ���������� ������
                byte[] data = new byte[256]; // ����� ��� ���������� ������

                // �����, � �������� ������ ������
                EndPoint remoteIp = new IPEndPoint(IPAddress.Any, 0);

                do
                {
                    bytes = listeningSocket.ReceiveFrom(data, ref remoteIp);
                }
                while (listeningSocket.Available > 0);

                // �������� ������ � �����������
                IPEndPoint remoteFullIp = remoteIp as IPEndPoint;

                // ������������ ������ 4 ����� � ����� �����
                if (bytes >= 4)
                {
                    int receivedInt = BitConverter.ToInt32(data, 0);
                    Console.WriteLine($"{Client.listeningSocket.LocalEndPoint}");
                    // �������� ��� ����� � �������
                    DisplayImageInPictureBox(receivedInt, comparisonImages);
                }
            }
        }





        // ���� ����� ���������� ����� ����, ��� ����������� �������
        private  void DisplayImageInPictureBox(int imageIndex, List<byte[]> comparisonImages)
        {
            mirrorPictureBox.Image = null;
            if(imageIndex != -1){
                using (MemoryStream ms = new MemoryStream(comparisonImages[imageIndex]))
                {
                    // ������� ����������� �� ������
                    Image image = Image.FromStream(ms);

                    // �����������, ��� � ��� ���� PictureBox �� �����, ��������, mirrorPictureBox
                    mirrorPictureBox.Image = image;
                }
            }
            else
            {
                string noImagePath = "no.png"; // ���������, ��� ���� � ����� ������
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
               
                //// �������� ���������� �����������
                SendImages(referenceImage,comparisonImages.Count);

                // �������� ����������� ��� ���������
                foreach (var image in comparisonImages)
                {
                    SendImages(image, comparisonImages.Count);
                }
                byte[] doneMessage = Encoding.Unicode.GetBytes("done");
                listeningSocket.Send(doneMessage);
                // ��������� ����� ��� �������������
                Task listeningTask = new Task(Listen);
                listeningTask.Start();
            
        }

       public void SendImages(byte[] imageData, int totalImages)
        {
            int totalPackets = (int)Math.Ceiling((double)imageData.Length / maxPacketSize);

            // �������� ����������� � �������
            for (int i = 0; i < totalPackets; i++)
            {
                int size = Math.Min(maxPacketSize, imageData.Length - i * maxPacketSize);
                byte[] packet = new byte[size + 12]; // 12 ����: ����� ������, ����� ���������� �������, ����� ���������� �����������

                // ��������� ����� ������
                BitConverter.GetBytes(i).CopyTo(packet, 0);

                // ��������� ����� ���������� �������
                BitConverter.GetBytes(totalPackets).CopyTo(packet, 4);

                // ��������� ����� ���������� �����������
                BitConverter.GetBytes(totalImages).CopyTo(packet, 8);

                // �������� ������ �����������
                Array.Copy(imageData, i * maxPacketSize, packet, 12, size);

                // ���������� �����
                listeningSocket.Send(packet);
                Console.WriteLine($"��������� ����� {i + 1}/{totalPackets}, ������ ������: {size + 12} ����");
                Thread.Sleep(5);
            }
            Thread.Sleep(20);
            Console.WriteLine("����������� ����������.");
        }
    }

}
