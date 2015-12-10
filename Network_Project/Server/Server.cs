using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Server
{
    class Server
    {
        // Server Socket
        private static Socket _serverSocket;
        // List of client sockets
        private static readonly List<Socket> _clientSockets = new List<Socket>();
        private const int _BUFFER_SIZE = 2048;
        private static readonly byte[] _buffer = new byte[_BUFFER_SIZE];
        private static string _port;

        static void Main(string[] args)
        {
            Console.Title = "Network Project - Server side";

            SetupServer();
            
            Console.WriteLine(@"<Press enter to close the server>");
            Console.ReadLine();
            
            CloseAllSockets();
        }

        // Get IP Address
        private static string GetIP4Address()
        {
            IPAddress[] ips = Dns.GetHostAddresses(Dns.GetHostName());

            foreach (IPAddress i in ips)
            {
                if (i.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return i.ToString();
            }
            return "127.0.0.1";
        }

        // Setup the server with the listening port
        private static void SetupServer()
        {
            string ip = GetIP4Address();

            try
            {
                // Parsing config with config.xml file
                using (XmlTextReader reader = new XmlTextReader(@".\config.xml"))
                {
                    reader.ReadToFollowing("ListeningPort");
                    _port = reader.ReadElementContentAsString();
                }
            }
            catch (FileNotFoundException ex) // Ask port when config.xml is not found
            {
                Console.WriteLine("config.xml file not found !");
                Console.Write("Enter the listening port: ");
                _port = Console.ReadLine();
            }

            // Creating the server socket
            _serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            // Setting up the listening IP address and listening port
            _serverSocket.Bind(new IPEndPoint(IPAddress.Parse(ip), int.Parse(_port)));
            _serverSocket.Listen(5);
            _serverSocket.BeginAccept(AcceptCallback, null);
            WriteLog("Starting server listening on " + ip + ":" + _port);
        }

        // Close all connected client
        private static void CloseAllSockets()
        {
            foreach (Socket socket in _clientSockets)
            {
                socket.Shutdown(SocketShutdown.Both);
                socket.Close();
            }

            _serverSocket.Close();
        }

        // Accept a new client
        private static void AcceptCallback(IAsyncResult AR)
        {
            Socket socket;

            try
            {
                socket = _serverSocket.EndAccept(AR);
            }
            catch (ObjectDisposedException) // I cannot seem to avoid this (on exit when properly closing sockets)
            {
                return;
            }

            _clientSockets.Add(socket);
            socket.BeginReceive(_buffer, 0, _BUFFER_SIZE, SocketFlags.None, ReceiveCallback, socket);
            WriteLog("Client connected, waiting for request...");
            _serverSocket.BeginAccept(AcceptCallback, null);
        }

        // Receive a callback from a client
        private static void ReceiveCallback(IAsyncResult AR)
        {
            Socket current = (Socket)AR.AsyncState;
            
            int received;

            try
            {
                received = current.EndReceive(AR);
            }
            catch (SocketException) // Disconnection from the client by closing window
            {
                WriteLog("Client forcefully disconnected");
                current.Close();
                _clientSockets.Remove(current);
                return;
            }

            byte[] recBuf = new byte[received];
            Array.Copy(_buffer, recBuf, received);
            string text = Encoding.ASCII.GetString(recBuf);

            // Log the request with its IP address
            WriteLog("Received from " + ((IPEndPoint)current.RemoteEndPoint).Address.ToString() + ": " + text);

            if(text.ToLower() == "list") // Client requested list
            {
                WriteLog("List request");

                string filesList = "Files in the server side";
                try
                {
                    var files = Directory.EnumerateFiles(@".\files", "*", SearchOption.AllDirectories);

                    filesList += " (" + files.Count().ToString() + " file" + ((files.Count() > 1) ? "s" : "") + " found)\n--\n";

                    foreach (string currentFile in files)
                    {
                        filesList += currentFile.Replace(@".\files\", "").Replace(".encrypted", "") + "\n";
                    }
                    filesList += "\n";
                }
                catch (Exception e)
                {
                    WriteLog(e.Message);
                }

                byte[] data = Encoding.ASCII.GetBytes(filesList);
                current.Send(data);
                WriteLog("List sent to client");
            }
            else if(text.ToLower().Contains("send")) // Receive a file from a client
            {
                WriteLog("Receiving file...");
                byte[] clientData = new byte[1024 * 5000];

                //This needs to be redone, see my first point
                int receivedBytesLen = current.Receive(clientData);
                int fileNameLen = BitConverter.ToInt32(clientData, 0);
                string fileName = Encoding.ASCII.GetString(clientData, 4, fileNameLen);

                // "using" for files with a big size
                using (BinaryWriter bWrite = new BinaryWriter(File.Open(@".\files\" + fileName, FileMode.Append)))
                {
                    bWrite.Write(clientData, 4 + fileNameLen, receivedBytesLen - 4 - fileNameLen);
                }

                // Rename filename in the list for showing to the client
                fileName = fileName.Replace(".encrypted", "");

                byte[] data = Encoding.ASCII.GetBytes("File sent with success: " + fileName);
                current.Send(data);
                WriteLog("File received with success: " + fileName);
            }
            else if(text.ToLower().Contains("get")) // Send a file to a client
            {
                string filename = text.Substring(4) + ".encrypted";

                try
                {
                    string filePath = @".\files\";
                    string fileName = filename.Replace("\\", "/");
                    while (fileName.IndexOf("/") > -1)
                    {
                        filePath += fileName.Substring(0, fileName.IndexOf("/") + 1);
                        fileName = fileName.Substring(fileName.IndexOf("/") + 1);
                    }

                    byte[] fileNameByte = Encoding.ASCII.GetBytes(fileName);

                    byte[] fileData = File.ReadAllBytes(filePath + fileName);
                    byte[] clientData = new byte[4 + fileNameByte.Length + fileData.Length];
                    byte[] fileNameLen = BitConverter.GetBytes(fileNameByte.Length);

                    fileNameLen.CopyTo(clientData, 0);
                    fileNameByte.CopyTo(clientData, 4);
                    fileData.CopyTo(clientData, 4 + fileNameByte.Length);

                    // Send data to the client
                    current.Send(clientData);
                }
                catch(FileNotFoundException ex)
                {
                    byte[] data = Encoding.ASCII.GetBytes("File not found: " + filename);
                    current.Send(data);
                    WriteLog("File not found: " + filename);
                }
            }
            else if(text.ToLower().Contains("endtransfer")) // end of a transfer to confirm the success/error
            {
                string fileName = text.Substring(12);
                if(fileName == "error")
                {
                    byte[] data = Encoding.ASCII.GetBytes("An error occurred during the process of receiving file!");
                    current.Send(data);
                    WriteLog("An error occurred during the process of receiving file!");
                }
                else
                {
                    byte[] data = Encoding.ASCII.GetBytes("File received with success: " + fileName);
                    current.Send(data);
                    WriteLog("File send with success: " + fileName);
                }
            }
            else if(text.ToLower() == "help") // Show help
            {
                WriteLog("Help commands");

                string help = "Help\n";
                help += "--\n";
                help += "list\t\t\tList the files\n";
                help += "send <filename>\t\tSend a file to the server\n";
                help += "get <filename>\t\tGet a file from the server\n";
                help += "help\t\t\tDisplay available commands\n";
                help += "exit\t\t\tQuit & Exit\n";

                byte[] data = Encoding.ASCII.GetBytes(help);
                current.Send(data);
            }
            else if(text.ToLower() == "exit") // Exit command
            {
                // Always Shutdown before closing
                current.Shutdown(SocketShutdown.Both);
                current.Close();
                _clientSockets.Remove(current);
                WriteLog("Client disconnected");
                return;
            }
            else
            {
                WriteLog("Text is an invalid request");
                byte[] data = Encoding.ASCII.GetBytes(@"Invalid request: type ""help"" to display available commands!");
                current.Send(data);
                WriteLog("Warning Sent");
            }

            current.BeginReceive(_buffer, 0, _BUFFER_SIZE, SocketFlags.None, ReceiveCallback, current);
        }

        // Write to the console with the date and time
        public static void WriteLog(string txt)
        {
            Console.WriteLine("[" + DateTime.Now.ToShortDateString() + " - " + DateTime.Now.ToLongTimeString() + "] " + txt);
        }
    }
}
