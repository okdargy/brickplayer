using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Utils;

public class NetworkManager : MonoBehaviour
{
    public string ServerIP;
    public int ServerPort;
    public int GameID;
    public string CookieToken;
    public string TokenAPIUrl;

    //public int totalReadBytes = 0;
    //public int totalReadBytesExclSize = 0;
    //public int totalProcessedBytes = 0;

    private const string ClientVersion = "0.3.0.3";

    private string authToken = "";

    public PlayerMain main;

    private TcpClient socketConnection;
    private Thread clientRecieveThread;

    private bool attemptedAuthentication = false;

    private List<byte[]> packetQueue = new List<byte[]>();
    private bool pausePacketHandling;

    private int currentPacketSize = 0;
    private byte[] currentPacket;
    private int readBytes = 0;

    private string dataPath;

    private bool showMessaage;
    private string messageTitle = "";
    private string messageText = "";

    private void Awake () {
        Application.runInBackground = true;
    }

    private void Update () {
        if (packetQueue.Count > 0 && !pausePacketHandling) {
            HandlePacketQueue();
        }

        if (showMessaage) {
            main.ui.ShowMessage(messageTitle, messageText);
            showMessaage = false;
            messageTitle = "";
            messageText = "";
        }
    }

    private void Start() {
        if (Application.platform == RuntimePlatform.Android) {
            dataPath = Application.persistentDataPath;
        } else {
            dataPath = Application.dataPath;
        }

        // debug: force load resourcepack
        //CustomResourceManager.ServerSubfolder = GameID + "/";
        //CustomResourceManager.CheckForResources();

        string[] args = System.Environment.GetCommandLineArgs();
        if (args.Length > 1) {
            string launchArg = args[args.Length - 1];
            if (launchArg.StartsWith("BHCONNECT/")) {
                string[] connectInfo = launchArg.Split('/');
                if (connectInfo.Length == 5) {
                    authToken = connectInfo[1];
                    ServerIP = connectInfo[2];
                    ServerPort = Int32.Parse(connectInfo[3], CultureInfo.InvariantCulture);
                    GameID = Int32.Parse(connectInfo[4], CultureInfo.InvariantCulture);

                    //File.WriteAllLines("B:/bpe.txt", connectInfo);

                    ConnectToServer(ServerIP, ServerPort, authToken);
                    main.ui.SetGameUI(true);
                    return;
                }
            }
        }

        if (ServerIP != "") {
            AuthenticateAndConnect(); // dc args are set in inspector
            main.ui.SetGameUI(true);
        } else {
            main.ui.ShowDCPanel(); // dc args aren't set
        }
    }

    public void AuthenticateAndConnect () {
        Debug.Log("Attempting to get authentication token...");
        StartCoroutine(GetAuthToken(GameID, c => {
            if (c != "") {
                AuthTokenResponse atr = JsonUtility.FromJson<AuthTokenResponse>(c);
                if (atr.token != null) {
                    Debug.Log("Successfully got auth token probably!");
                    ConnectToServer(ServerIP, ServerPort, atr.token);
                } else {
                    Debug.Log("Could not authenticate - are you logged in?");
                }
            }
        }));
    }

    IEnumerator GetAuthToken (int id, System.Action<string> callback) {
        string url = TokenAPIUrl + id.ToString();
        //string decodedCookies = Encoding.ASCII.GetString(Convert.FromBase64String(CookieToken)); // decode base64 string
        string decodedCookies = CookieToken;
        using (UnityWebRequest request = UnityWebRequest.Get(url)) {
            request.SetRequestHeader("cookie", decodedCookies); // log into website
            yield return request.SendWebRequest();
            if (request.isNetworkError) {
                callback("");
            } else {
                callback(request.downloadHandler.text);
            }
        }
    }

    public void ConnectToServer (string ip, int port, string auth) {
        if (ip == "local") ip = "127.0.0.1"; // when launching locally, the ip is passed as "local"
        try {
            // connect
            socketConnection = new TcpClient(ip, port);
            Debug.Log("Connected to server.");

            // authenticate
            PacketBuilder b = new PacketBuilder((byte)ClientPackets.Authentication);
            b.AppendString(auth); // Authentication Token
            b.AppendString(ClientVersion); // self explanatory :)
            b.AppendBool(true); // indicates brickplayer client
            SendData(b.GetBytesCompressed());
            Debug.Log("Send authentication packet");

            // listen
            clientRecieveThread = new Thread(new ThreadStart(ListenForData));
            clientRecieveThread.IsBackground = true;
            clientRecieveThread.Start();
            Debug.Log("Started recieve thread");
        } catch (Exception e) {
            Debug.LogException(e);
            DisconnectFromServer("Error connecting to server.");
        }
    }

    public void DisconnectFromServer (string message) {
        if (socketConnection == null || !socketConnection.Connected) return;
        messageText = message;
        socketConnection.Close();
        Debug.Log("Closed socket.");
    }

    public void CleanUpAfterDC () {
        packetQueue.Clear();
        Debug.Log("Cleaned up after DC. Disconnected?");
    }

    private void ListenForData () {
        try {
            byte[] buffer = new byte[16384];
            while (socketConnection.Connected) {
                using (NetworkStream stream = socketConnection.GetStream()) {
                    int len; // length of buffer
                    currentPacketSize = 0; // length of current packet

                    currentPacket = null; // bytes of current packet (used when packet is split between buffers)
                    readBytes = 0;

                    while ((len = stream.Read(buffer, 0, 16384)) != 0) {
                        // reading stream
                        if (currentPacket != null) {
                            // current packet array is not null, which means an incomplete packet was read. we need to read more bytes to it
                            int bytesToRead = currentPacketSize - readBytes; // amount of bytes left to read
                            if (bytesToRead > len) {
                                //Debug.Log("Recieved Part Of Split Packet");
                                // packet is still not finished, keep reading
                                Array.Copy(buffer, 0, currentPacket, readBytes, len);
                                readBytes += len;
                            } else {
                                //Debug.Log("Recieved End Of Split Packet");
                                // packet can be finished, finish then handle
                                Array.Copy(buffer, 0, currentPacket, readBytes, bytesToRead);

                                packetQueue.Add((byte[])currentPacket.Clone()); // copy byte array and add to queue
                                currentPacket = null;
                                readBytes = 0;
                            }
                        } else {
                            int packetSizeBytes = ByteLength(buffer[0]); // get amount of bytes the packet size uses
                            byte[] packetSize = new byte[packetSizeBytes]; // create array to hold packet size bytes
                            Array.Copy(buffer, 0, packetSize, 0, packetSizeBytes); // copy packet size bytes to array
                            currentPacketSize = ByteArrayToSize(packetSize, packetSizeBytes); // get length of packet

                            if (currentPacketSize == (len - packetSizeBytes)) {
                                //Debug.Log("Recieved Single Packet");
                                // entire buffer is a single packet
                                currentPacket = new byte[currentPacketSize];
                                Array.Copy(buffer, packetSizeBytes, currentPacket, 0, currentPacketSize);

                                packetQueue.Add((byte[])currentPacket.Clone()); // copy byte array and add to queue
                                currentPacket = null;
                            } else if (currentPacketSize < (len - packetSizeBytes)) {
                                //Debug.Log("Recieved Multiple Packets");
                                // buffer contains multiple packets
                                int currentByteIndex = 0;
                                while (currentByteIndex < len) {
                                    // here we are repeating what we originally did, since each packet likely will not have the same size
                                    packetSizeBytes = ByteLength(buffer[currentByteIndex]);
                                    if (packetSizeBytes == 4) {
                                        File.WriteAllText(dataPath + "/bufferout.txt", Helper.PrintBytes(buffer)); // B:/bufferout.txt - /home/ty/bufferout.txt
                                        Debug.Log("wrote bufferout");
                                    }
                                    packetSize = new byte[packetSizeBytes];
                                    Array.Copy(buffer, currentByteIndex, packetSize, 0, packetSizeBytes);
                                    currentPacketSize = ByteArrayToSize(packetSize, packetSizeBytes);

                                    currentPacket = new byte[currentPacketSize];
                                    Array.Copy(buffer, currentByteIndex + packetSizeBytes, currentPacket, 0, currentPacketSize); // sometimes doesnt work
                                    currentByteIndex += currentPacketSize + packetSizeBytes;

                                    packetQueue.Add((byte[])currentPacket.Clone()); // copy byte array and add to queue
                                    currentPacket = null;
                                }
                            } else {
                                //Debug.Log("Recieved Start Of Split Packet");
                                // current packet is split between multiple buffers
                                currentPacket = new byte[currentPacketSize];
                                Array.Copy(buffer, packetSizeBytes, currentPacket, 0, len - packetSizeBytes);
                                readBytes = len - packetSizeBytes;
                            }
                        }
                    }
                }
            }
            // this code is exceuted after socket is disconnected
            CleanUpAfterDC();
            showMessaage = true;
            messageTitle = "Alert";
            if (messageText == "") messageText = "You have been disconnected.";
        } catch (IOException e) {
            Debug.LogException(e);
            if (socketConnection.Connected) socketConnection.Close(); // this shouldnt happen but i have it just in case
            CleanUpAfterDC();

            showMessaage = true;
            messageTitle = "Alert";
            if (messageText == "") messageText = "You have been disconnected.";
        } catch (Exception e) {
            Debug.LogException(e);
            if (socketConnection.Connected) socketConnection.Close();
            CleanUpAfterDC();

            showMessaage = true;
            messageTitle = "Alert";
            messageText = "An error has occured while reading packets.";
        }      
    }

    public void SendData (byte[] data) {
        if (socketConnection == null || !socketConnection.Connected) return;
        try {
            NetworkStream stream = socketConnection.GetStream();
            if (stream.CanWrite) {
                stream.Write(data, 0, data.Length);
            }
        } catch (Exception e) {
            Debug.LogException(e);
        }
    }

    public void SetPacketHandlerState (bool value) {
        pausePacketHandling = !value;
    }

    // BH related functions

    public void HandlePacketQueue () {
        for (int i = 0; i < packetQueue.Count; i++) {
            if (packetQueue[i] == null) continue;
            if (pausePacketHandling) return;
            try {
                //BufferReader br = new BufferReader(packetQueue[i]);
                PacketReader r = new PacketReader(packetQueue[i]);
                if (r.BufferLength == 0) continue; // how
                byte packetType = r.ReadByte();

                //bool countPacket = true;

                //File.AppendAllText("B:/how.txt", packetType.ToString());

                //Debug.Log("Handling packet! Type: " + (int)packetType);

                switch (packetType) {
                    case (byte)ServerPackets.Authentication:
                        // game info
                        Debug.Log("recived game data");
                        main.SetGameInfo(r);
                        break;
                    case (byte)ServerPackets.Bricks:
                        // send brk data
                        main.LoadBricks(r);
                        Debug.Log("loading brk data!");
                        //countPacket = false;
                        break;
                    case (byte)ServerPackets.Players:
                        // send playerlist
                        main.PopulatePlayerlist(r);
                        break;
                    case (byte)ServerPackets.UpdatePlayer:
                        // figure
                        main.AddFigureInfo(r);
                        break;
                    case (byte)ServerPackets.PlayerLeft:
                        // remove player?
                        main.RemovePlayer(r);
                        break;
                    case (byte)ServerPackets.Chat:
                        // chat message
                        main.LogChatMessage(r);
                        //countPacket = false;
                        break;
                    case (byte)ServerPackets.Settings:
                        // gui / settings?
                        main.SetSettings(r);
                        break;
                    case (byte)ServerPackets.Kill:
                        // death status
                        main.SetDeathStatus(r);
                        break;
                    case (byte)ServerPackets.UpdateBrick:
                        // brick props
                        main.SetBrickProps(r);
                        //Debug.Log(Helper.PrintBytes(br.buffer));
                        break;
                    case (byte)ServerPackets.Team:
                        // create team?
                        main.CreateTeam(r);
                        break;
                    case (byte)ServerPackets.Tool:
                        // add tool
                        main.CreateTool(r);
                        break;
                    case (byte)ServerPackets.Bot:
                        // bot?
                        main.ModifyBot(r);
                        break;
                    case (byte)ServerPackets.Projectile:
                        // projectile?
                        Debug.Log("projectile: " + Helper.PrintBytes(r.GetBytes()));
                        break;
                    case (byte)ServerPackets.Clear:
                        // clear map?
                        main.ClearMap();
                        break;
                    case (byte)ServerPackets.DeleteBot:
                        // delete bot?
                        Debug.Log("delete bot: " + Helper.PrintBytes(r.GetBytes()));
                        break;
                    case 0x10:
                        // delete brick?
                        Debug.Log("Delete brick: " + Helper.PrintBytes(r.GetBytes()));
                        break;
                    case (byte)ServerPackets.BrickPlayer:
                        // bp custom packet
                        string customType = r.ReadString();
                        if (customType == "resourcepack") {
                            CustomResourceManager.DownloadResourcePack(this, r);
                        } else if (customType == "audio") {
                            AudioManager.PlaySoundPacket(r);
                        }
                        break;
                    case 0x78:
                        // compressed bytes - probably map data or chat
                        byte[] decompressedBytes = Ionic.Zlib.ZlibStream.UncompressBuffer(packetQueue[i]); // decompress packet
                        packetQueue.Add(decompressedBytes); // add to queue
                        break;
                }

                //if (countPacket)
                    //totalProcessedBytes += r.BufferLength + 1;
                
                packetQueue[i] = null; // sometimes doesnt work
            } catch (Exception e) {
                Debug.LogException(e);
                continue;
            }
        }
        packetQueue.Clear();
    }

    public bool IsConnected () {
        return socketConnection != null && socketConnection.Connected;
    }

    // helper functions

    public static int ByteLength (byte buffer) {
        if ((buffer & 1) != 0) {
            return 1; // uint8
        } else if ((buffer & 2) != 0) {
            return 2; // uint16
        } else if ((buffer & 4) != 0) {
            return 3; // uint8+uint16
        } else {
            return 4; // uint32
        }
    }

    public static int ByteArrayToSize (byte[] size, int len) {
        switch(len) {
            case 1:
                return (size[0]-1)>>1;
            case 2:
                return ((BitConverter.ToUInt16(size,0)-2)>>2)+128;
            case 3:
                return (size[2] << 13) + (size[1] << 5) + (size[0] >> 3) + 16512;
            case 4:
                return (int)((BitConverter.ToUInt32(size,0)/(uint)8)+2113664);
        }
        return 0;
    }

    public enum ClientPackets : byte {
        Authentication = 0x01,
        Movement = 0x02,
        Command = 0x03,
        Click = 0x05,
        Input = 0x06
    }

    public enum ServerPackets : byte {
        Authentication = 0x01,
        Bricks = 0x02,
        Players = 0x03,
        UpdatePlayer = 0x04,
        PlayerLeft = 0x05,
        Chat = 0x06,
        Settings = 0x07,
        Kill = 0x08,
        UpdateBrick = 0x09,
        Team = 0x0a,
        Tool = 0x0b,
        Bot = 0x0c,
        Projectile = 0x0d,
        Clear = 0x0e,
        DeleteBot = 0x0f,
        BrickPlayer = 0x1a
    }
}

// this is so we can ez parse the auth json with the builtin json library
public class AuthTokenResponse {
    public string token;
}

// easily create byte arrays
public class PacketBuilder {
    private List<byte> bytes = new List<byte>();

    public PacketBuilder (byte type) {
        if (type != 0x00)
            bytes.Add(type);
    }

    public PacketBuilder AppendByte (byte data) {
        bytes.Add(data);
        return this;
    }

    public PacketBuilder AppendBytes (byte[] data) {
        bytes.AddRange(data);
        return this;
    }

    public PacketBuilder AppendUInt (uint data) {
        bytes.AddRange(BitConverter.GetBytes(data));
        return this;
    }

    public PacketBuilder AppendInt (int data) {
        bytes.AddRange(BitConverter.GetBytes(data));
        return this;
    }

    public PacketBuilder AppendFloat (float data) {
        bytes.AddRange(BitConverter.GetBytes(data));
        return this;
    }

    public PacketBuilder AppendString (string data) {
        if (data != "" && data != null) {
            byte[] stringBytes = Encoding.UTF8.GetBytes(data);
            bytes.AddRange(stringBytes);
        }
        bytes.Add(0x00); // null terminator, marks end of string
        return this;
    }

    public PacketBuilder AppendBool (bool data) {
        bytes.Add(data ? (byte)0x01 : (byte)0x00);
        return this;
    }

    public void Clear () {
        bytes.Clear();
    }

    public byte[] GetBytes () {
        List<byte> temporaryBytes = new List<byte>();
        temporaryBytes.AddRange(Networking.GetPacketSize(bytes.Count));
        temporaryBytes.AddRange(bytes);
        return temporaryBytes.ToArray();
    }

    public byte[] GetBytesCompressed () {
        byte[] compressedBytes = Ionic.Zlib.ZlibStream.CompressBuffer(bytes.ToArray());
        List<byte> temporaryBytes = new List<byte>();
        temporaryBytes.AddRange(Networking.GetPacketSize(compressedBytes.Length));
        temporaryBytes.AddRange(compressedBytes);
        return temporaryBytes.ToArray();
    }

    public PacketBuilder InsertByte (byte data, int position) {
        bytes.Insert(position, data);
        return this;
    }
}

// easily read byte arrays
public class PacketReader {
    private byte[] bytes;
    public int index = 0;

    public int BufferLength { get { return bytes.Length; } }

    public PacketReader (byte[] source) {
        bytes = source;
    }

    public bool Decompress () {
        try {
            if (bytes[0] == 0x78) {
                // this packet is indeed compressed, time to decompress
                byte[] decompressed = Ionic.Zlib.ZlibStream.UncompressBuffer(bytes);
                bytes = decompressed;
                index = 0;
                return true;
            } else {
                // this packet is not compressed
                return false;
            }
        } catch (Exception e) {
            // error during decompression
            Debug.LogException(e);
            return false;
        }
    }

    public bool Finished () {
        return index >= bytes.Length;
    }

    public bool IsCompressed () {
        return bytes[0] == 0x78; // magic number
    }

    public byte[] GetBytes () {
        return bytes;
    }

    public byte ReadByte () {
        return bytes[index++];
    }

    public uint ReadUInt () {
        if (index+3 < bytes.Length) {
            byte[] sub = new byte[4];
            Array.Copy(bytes, index, sub, 0, 4);
            index += 4;
            return BitConverter.ToUInt32(sub, 0);
        }
        return 0;
    }

    public int ReadInt () {
        if (index+3 < bytes.Length) {
            byte[] sub = new byte[4];
            Array.Copy(bytes, index, sub, 0, 4);
            index += 4;
            return BitConverter.ToInt32(sub, 0);
        }
        return 0;
    }

    public float ReadFloat () {
        if (index+3 < bytes.Length) {
            byte[] sub = new byte[4];
            Array.Copy(bytes, index, sub, 0, 4);
            index += 4;
            return BitConverter.ToSingle(sub, 0);
        }
        return 0;
    }

    public string ReadString () {
        int len = 0;
        for (int i = index; i < bytes.Length; i++) {
            if (bytes[i] == 0x00) break; // reached end of string
            len++;
        }
        if (len > 0) {
            byte[] sub = new byte[len];
            Array.Copy(bytes, index, sub, 0, len);
            index += len+1;
            return Encoding.UTF8.GetString(sub);
        }
        index++;
        return "";
    }

    public bool ReadBool () {
        byte val = bytes[index++];
        return val == 0x00 ? false : true;
    }
}
