using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Client
{
    class Client
    {
        // Client Socket
        private static readonly Socket _clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private static string _ip = "";
        private static string _port = "";

        static void Main(string[] args)
        {
            Console.Title = "Network Project - Client side";
            ConnectToServer();
            RequestLoop();
            Exit();
        }

        private static void ConnectToServer()
        {
            // Number of attemps to connect to the server
            int attempts = 0;

            try
            {
                // Parsing config.xml file to get the ip address and port of the server
                using (XmlTextReader reader = new XmlTextReader(@".\config.xml"))
                {
                    reader.ReadToFollowing("ip");
                    _ip = reader.ReadElementContentAsString();

                    reader.ReadToFollowing("port");
                    _port = reader.ReadElementContentAsString();
                }
            }
            catch(FileNotFoundException ex) // Request ip and port if config.xml doesn't exist
            {
                Console.WriteLine("config.xml file not found !");
                Console.Write("Enter host IP address : ");
                _ip = Console.ReadLine();
                Console.Write("Enter host port : ");
                _port = Console.ReadLine();
            }

            while (!_clientSocket.Connected)
            {
                try
                {
                    attempts++;
                    Console.WriteLine("Connection attempt " + attempts);

                    // Connect to the server
                    _clientSocket.Connect(IPAddress.Parse(_ip), int.Parse(_port));
                }
                catch (SocketException)
                {
                    Console.Clear();
                }
            }

            Console.Clear();
            Console.WriteLine("Connected on " + _ip + ":" + _port);
        }

        // Loop for request & response
        private static void RequestLoop()
        {
            Console.WriteLine(@"<Type ""help"" to display available commands>");

            while (true)
            {
                SendRequest();
                ReceiveResponse();
            }
        }

        // Close socket and exit app
        private static void Exit()
        {
            SendString("exit"); // Tell the exit command to the server
            _clientSocket.Shutdown(SocketShutdown.Both);
            _clientSocket.Close();
            Environment.Exit(0);
        }

        // Send a request to the server
        private static void SendRequest()
        {
            Console.Write(_ip + ":" + _port + " > ");
            string request = Console.ReadLine();
            SendString(request);

            if (request.ToLower() == "exit")
            {
                Exit();
            }
            else if(request.ToLower() == "list")
            {
                string filesList = "Files in the client side";
                try
                {
                    var files = Directory.EnumerateFiles(@".\files", "*", SearchOption.AllDirectories);

                    filesList += " (" + files.Count().ToString() + " file" + ((files.Count() > 1) ? "s" : "") + " found)\n--\n";

                    foreach (string currentFile in files)
                    {
                        filesList += currentFile.Replace(@".\files\", "").Replace(".encrypted", "") + "\n";
                    }
                    filesList += "\n";
                    Console.WriteLine(filesList);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
            else if(request.ToLower().Contains("send"))
            {
                string filename = request.Substring(5);

                Console.Write("Passphrase to encrypt the file: ");
                string password = Console.ReadLine();

                SendFile(filename, password);
            }
            else if(request.ToLower().Contains("get"))
            {
                Console.Write("Passphrase to decrypt the file: ");
                string password = Console.ReadLine();

                Console.WriteLine("Receiving file...");

                string filename = ReceiveFile(request.Substring(4), password);
                SendString("endTransfer " + filename);
            }
        }

        // Sends a string to the server
        private static void SendString(string text)
        {
            byte[] buffer = Encoding.ASCII.GetBytes(text);
            try
            {
                _clientSocket.Send(buffer, 0, buffer.Length, SocketFlags.None);
            }
            catch(SocketException ex)
            {
                Console.WriteLine("The server has disconnected!");
                Console.ReadLine();
                Exit();
            }
        }

        // Sends a file to the server
        private static void SendFile(string filename, string password)
        {
            try
            {
                string filePath = @".\files\";
                string fileName = filename.Replace("\\", "/");
                string fileNameEncrypted = fileName + ".encrypted";

                // Encrypt the file with the password
                CryptClass.EncryptFile(password, filePath + fileName, filePath + fileNameEncrypted);

                while (fileNameEncrypted.IndexOf("/") > -1)
                {
                    filePath += fileNameEncrypted.Substring(0, fileNameEncrypted.IndexOf("/") + 1);
                    fileNameEncrypted = fileNameEncrypted.Substring(fileNameEncrypted.IndexOf("/") + 1);
                }

                byte[] fileNameEncryptedByte = Encoding.ASCII.GetBytes(fileNameEncrypted);

                byte[] fileData = File.ReadAllBytes(filePath + fileNameEncrypted);
                byte[] clientData = new byte[4 + fileNameEncryptedByte.Length + fileData.Length];
                byte[] fileNameEncryptedLen = BitConverter.GetBytes(fileNameEncryptedByte.Length);

                fileNameEncryptedLen.CopyTo(clientData, 0);
                fileNameEncryptedByte.CopyTo(clientData, 4);
                fileData.CopyTo(clientData, 4 + fileNameEncryptedByte.Length);

                // Send data to the server
                _clientSocket.Send(clientData);

                // Delete the temp encrypted file
                File.Delete(filePath + fileNameEncrypted);
            }
            catch (SocketException ex)
            {
                Console.WriteLine("The server has disconnected!");
                Console.ReadLine();
                Exit();
            }
        }

        // Receive a response from the server
        private static void ReceiveResponse()
        {
            try
            {
                var buffer = new byte[2048];
                int received = _clientSocket.Receive(buffer, SocketFlags.None);
                if (received == 0) return;

                var data = new byte[received];
                Array.Copy(buffer, data, received);
                string text = Encoding.ASCII.GetString(data);
                Console.WriteLine(text);
            }
            catch (SocketException ex)
            {
                Console.WriteLine("The server has disconnected!");
                Console.ReadLine();
                Exit();
            }
        }

        // Receive a file from the server
        private static string ReceiveFile(string filename, string password)
        {
            try
            {
                string filePath = @".\files\";

                byte[] clientData = new byte[1024 * 5000];

                //This needs to be redone, see my first point
                int receivedBytesLen = _clientSocket.Receive(clientData);
                if (receivedBytesLen == 0) return "error";

                string fileName;

                int fileNameLen = BitConverter.ToInt32(clientData, 0);
                fileName = Encoding.ASCII.GetString(clientData, 4, fileNameLen);
                // "using" for files with a big size
                using (BinaryWriter bWrite = new BinaryWriter(File.Open(filePath + fileName, FileMode.Append)))
                {
                    bWrite.Write(clientData, 4 + fileNameLen, receivedBytesLen - 4 - fileNameLen);
                }
                
                string filenameDecrypted = fileName.Replace(".encrypted", "");
                try
                {
                    // Decrypt the file with the password
                    CryptClass.DecryptFile(password, filePath + fileName, filePath + filenameDecrypted);
                }
                catch(CryptographicException ex)
                {
                    Console.WriteLine("The passphrase is not correct!");
                    // Delete the two files if the password is not correct and return an error
                    File.Delete(filePath + fileName);
                    File.Delete(filePath + filenameDecrypted);
                    return "error";
                }

                // Delete the temp encrypted file
                File.Delete(filePath + fileName);

                return filenameDecrypted;
            }
            catch (IOException ex)
            {
                Console.WriteLine("An error occurred during the transfer!");
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Console.WriteLine("File not found: " + filename);
            }
            catch (SocketException ex)
            {
                Console.WriteLine("The server has disconnected!");
                Console.ReadLine();
                Exit();
            }
            return "error";
        }
    }
}
