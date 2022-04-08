using BrickHill;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Utils;

public class PlayerMain : MonoBehaviour
{
    public static PlayerMain instance;

    public NetworkManager net;
    public MapManager map;
    public PlayerUI ui;

    public GameObject LocalCamera;
    public Transform LocalCameraTarget;
    public GameObject LocalPlayerPrefab;
    public GameObject NetworkPlayerPrefab;
    public GameObject BotPrefab;

    public Texture DefaultFace;

    public List<Player> Playerlist = new List<Player>();

    public Dictionary<int, GameObject> Characters = new Dictionary<int, GameObject>();
    public Dictionary<int, Team> Teams = new Dictionary<int, Team>();

    public Dictionary<int, GameObject> Bots = new Dictionary<int, GameObject>();

    public LocalInformation localInfo;

    public List<int> DeletedBricks = new List<int>();
    public List<int> CreatedBricks = new List<int>();

    private bool firstBrickLoad = true;

    void Awake () {
        if (instance == null) {
            instance = this;
        } else {
            Destroy(this);
        }
    }

    private void Start() {
        AudioListener.volume = SettingsManager.PlayerSettings.GlobalVolume;        
    }

    public void SetSettings (int index) {
        switch (index) {
            case 0:
            case 1:
            case 2:
                CharacterCamera cc = LocalCamera.GetComponent<CharacterCamera>();
                if (cc != null) {
                    cc.CameraSensitivity = SettingsManager.PlayerSettings.MouseSensitivity;
                    cc.ZoomSensitivity = SettingsManager.PlayerSettings.ZoomSensitivity;
                    cc.InvertZoom = SettingsManager.PlayerSettings.InvertZoom;
                }
                break;
            case 3: break; // movement keep thing
            case 4: AudioListener.volume = SettingsManager.PlayerSettings.GlobalVolume; break;
            case 5: QualitySettings.masterTextureLimit = (SettingsManager.PlayerSettings.TextureQuality + 1) % 2; break; // swaps 0 and 1, ez
            case 6: QualitySettings.shadows = (ShadowQuality)SettingsManager.PlayerSettings.Shadows; break;
            case 7: QualitySettings.shadowResolution = (ShadowResolution)SettingsManager.PlayerSettings.ShadowQuality; break;
            case 8: 
                int level = SettingsManager.PlayerSettings.Antialiasing;
                if (level == 3) { 
                    level = 8;
                } else {
                    level += level;
                } // bruh code
                QualitySettings.antiAliasing = level;
                break;
            case 9: LocalCamera.GetComponent<Camera>().farClipPlane = SettingsManager.PlayerSettings.DrawDistance; break;
            case 10: SetFramelimiter(SettingsManager.PlayerSettings.Framelimiter); break;            
            case 11: ui.SetUIBlur(SettingsManager.PlayerSettings.UIBlur); break;
            case 12: ui.SetFPSVisible(SettingsManager.PlayerSettings.ShowFPS); break;
        }
    }

    public void SetFramelimiter (int limit) {
        if (limit <= 14) {
            Application.targetFrameRate = -1; // no limit
        } else {
            Application.targetFrameRate = limit;
        }
    }

    // from networkmanager

    public void SetGameInfo (PacketReader r) {
        try {
            int netID = (int)r.ReadUInt();
            int brickCount = (int)r.ReadUInt();
            int playerId = (int)r.ReadUInt();
            string playerName = r.ReadString();
            bool admin = r.ReadByte() == 1;
            int membership = r.ReadByte();

            Player localPlayer = new Player() {
                NetID = netID,
                ID = playerId,
                Name = playerName,
                Admin = admin,
                Membership = membership
            };

            localInfo = new LocalInformation() {
                LocalPlayer = localPlayer,
                MapSize = brickCount
            };

            ui.SetLocalPlayerInfo(localPlayer);

            if (GetPlayer(localPlayer.NetID) == null) {
                Playerlist.Add(localPlayer);
                if (!Characters.ContainsKey(localPlayer.NetID)) CreateCharacter(localPlayer);
            }

            ui.UpdateGameInfo(localInfo);
            ui.UpdatePlayerlist();
        } catch (Exception e) {
            // PAH can't crash it now losers
            if (localInfo == null) {
                // what the heck man
                net.DisconnectFromServer("Recieved broken authentication packet, disconnected.");
            }
            Debug.LogException(e);
        }
    }

    public void PopulatePlayerlist (PacketReader r) {
        int playerCount = r.ReadByte();
        int currentPlayer = 0;
        while (currentPlayer < playerCount) {
            Player p = new Player() {
                NetID = (int)r.ReadUInt(),
                Name = r.ReadString(),
                ID = (int)r.ReadUInt(),
                Admin = r.ReadByte() == 1,
                Membership = r.ReadByte()
            };

            if (GetPlayer(p.NetID) == null) {
                Playerlist.Add(p);
                if (!Characters.ContainsKey(p.NetID)) CreateCharacter(p);
                Debug.Log("added new player!");
                currentPlayer++;
            } else {
                Debug.Log("tried to add existing player!");
            }
        }

        ui.UpdatePlayerlist();
    }

    public void CreateCharacter (Player p) {
        if (Characters.ContainsKey(p.NetID)) {
            Debug.Log("attempted to create already existing character");
            return; // dont add characters that already exist
        }
        CharacterMain cmain;
        if (p.NetID == localInfo.LocalPlayer.NetID) {
            // this is the local player
            GameObject localPlayer = Instantiate(LocalPlayerPrefab);
            localPlayer.name = p.Name + " (" + p.NetID + ")";

            cmain = localPlayer.GetComponent<CharacterMain>();
            cmain.main = this;
            cmain.player = p;

            CharacterMovement cmove = localPlayer.GetComponent<CharacterMovement>();
            cmove.Cam = LocalCameraTarget;

            CharacterCamera charCam = LocalCamera.GetComponent<CharacterCamera>();
            charCam.main = cmain;
            charCam.Player = localPlayer.transform;
            charCam.enabled = true;
            cmove.characterCamera = charCam;

            Characters.Add(p.NetID, localPlayer);
        } else {
            // this is a network player
            GameObject netPlayer = Instantiate(NetworkPlayerPrefab);
            netPlayer.name = p.Name + " (" + p.NetID + ")";

            cmain = netPlayer.GetComponent<CharacterMain>();
            cmain.main = this;
            cmain.player = p;
            cmain.BillboardTarget = LocalCamera.transform;
            cmain.SetNametag(p.Name, Color.white);

            Characters.Add(p.NetID, netPlayer);
        }

        cmain.UpdateFromFigure(true);
        CharacterAssetHelper.instance.GetAvatarInfo(p.ID, cmain.GetCharacterAssets);
    }

    public void DeleteCharacter (int netID) {
        if (Characters.TryGetValue(netID, out GameObject character)) {
            Characters.Remove(netID);
            Destroy(character);
        }
    }

    public void LogChatMessage (PacketReader r) {
        string message = r.ReadString();
        ui.LogChatMessage(message);
    }

    public void LoadBricks (PacketReader r) {
        if (firstBrickLoad) {
            if (localInfo != null) {
                ui.SetConnectingStatus(true, "Downloading " + localInfo.MapSize + " bricks...");
            } else {
                ui.SetConnectingStatus(true, "Downloading bricks...");
            }
        }

        string mapString = "";
        while (!r.Finished()) {
            mapString += r.ReadString();
        }
        map.LoadBricks(mapString);
        map.BuildBaseplate();

        if (firstBrickLoad) {
            ui.SetConnectingStatus(false);
            firstBrickLoad = false;
        }
    }

    public void SetSettings (PacketReader r) {
        string prop = r.ReadString();
        switch (prop) {
            case "topPrint":
                string topMessage = r.ReadString();
                int topTime = (int)r.ReadUInt();
                ui.PrintMessage(0, topMessage, topTime);
                break;
            case "centerPrint":
                string centerMessage = r.ReadString();
                int centerTime = (int)r.ReadUInt();
                ui.PrintMessage(1, centerMessage, centerTime);
                break;
            case "bottomPrint":
                string bottomMessage = r.ReadString();
                int bottomTime = (int)r.ReadUInt();
                ui.PrintMessage(2, bottomMessage, bottomTime);
                break;
            case "Ambient":
                Color ambientColor = new Color(r.ReadByte()/255f, r.ReadByte() / 255f, r.ReadByte() / 255f, r.ReadByte() / 255f);
                map.SetEnvironmentProperty(prop, ambientColor);
                break;
            case "Sky":
                Color skyColor = new Color(r.ReadByte() / 255f, r.ReadByte() / 255f, r.ReadByte() / 255f, r.ReadByte() / 255f);
                map.SetEnvironmentProperty(prop, skyColor);
                break;
            case "BaseCol":
                Color baseplateColor = new Color(r.ReadByte() / 255f, r.ReadByte() / 255f, r.ReadByte() / 255f, r.ReadByte() / 255f);
                map.SetEnvironmentProperty(prop, baseplateColor);
                break;
            case "BaseSize":
                int baseplateSize = (int)r.ReadUInt();
                map.SetEnvironmentProperty(prop, baseplateSize);
                break;
            case "Sun":
                int sunIntensity = (int)r.ReadUInt();
                map.SetEnvironmentProperty(prop, sunIntensity);
                break;
            case "kick":
                net.DisconnectFromServer("Kicked from server.");
                break;
            case "prompt":
                string promptMessage = r.ReadString();
                break;
            case "WeatherSnow":
                break;
            case "WeatherRain":
                break;
            case "WeatherSun":
                break;
        }
    }

    public void AddFigureInfo (PacketReader r) {
        int netID = (int)r.ReadUInt();
        string figureCode = r.ReadString();
        //Debug.Log(figureCode);
        //FigureData fig = new FigureData();
        Figure fig = new Figure();
        for (int i = 0; i < figureCode.Length; i++) {
            switch(figureCode[i]) {
                case 'A':
                    fig.Position.x = r.ReadFloat();
                    fig._updateValues[0] = true;
                    break;
                case 'B':
                    fig.Position.y = r.ReadFloat();
                    fig._updateValues[1] = true;
                    break;
                case 'C':
                    fig.Position.z = r.ReadFloat();
                    fig._updateValues[2] = true;
                    break;
                case 'D':
                    fig.Rotation.x = r.ReadFloat();
                    fig._updateValues[3] = true;
                    break;
                case 'E':
                    fig.Rotation.y = r.ReadFloat();
                    fig._updateValues[4] = true;
                    break;
                case 'F':
                    fig.Rotation.z = r.ReadFloat();
                    fig._updateValues[5] = true;
                    break;
                case 'G':
                    fig.Scale.x = r.ReadFloat();
                    fig._updateValues[6] = true;
                    break;
                case 'H':
                    fig.Scale.y = r.ReadFloat();
                    fig._updateValues[7] = true;
                    break;
                case 'I':
                    fig.Scale.z = r.ReadFloat();
                    fig._updateValues[8] = true;
                    break;
                case 'J':
                    fig.ToolSlotID = (int)r.ReadUInt();
                    fig._updateValues[9] = true;
                    break;
                case 'K':
                    fig.Colors.Head = Helper.DecToColor((int)r.ReadUInt());
                    fig._updateValues[10] = true;
                    break;
                case 'L':
                    fig.Colors.Torso = Helper.DecToColor((int)r.ReadUInt());
                    fig._updateValues[11] = true;
                    break;
                case 'M':
                    fig.Colors.LeftArm = Helper.DecToColor((int)r.ReadUInt());
                    fig._updateValues[12] = true;
                    break;
                case 'N':
                    fig.Colors.RightArm = Helper.DecToColor((int)r.ReadUInt());
                    fig._updateValues[13] = true;
                    break;
                case 'O':
                    fig.Colors.LeftLeg = Helper.DecToColor((int)r.ReadUInt());
                    fig._updateValues[14] = true;
                    break;
                case 'P':
                    fig.Colors.RightLeg = Helper.DecToColor((int)r.ReadUInt());
                    fig._updateValues[15] = true;
                    break;
                case 'Q':
                    fig.FaceID = (int)r.ReadUInt();
                    fig._updateValues[16] = true;
                    break;
                case 'U':
                    fig.HatID[0] = (int)r.ReadUInt();
                    fig._updateValues[17] = true;
                    break;
                case 'V':
                    fig.HatID[1] = (int)r.ReadUInt();
                    fig._updateValues[18] = true;
                    break;
                case 'W':
                    fig.HatID[2] = (int)r.ReadUInt();
                    fig._updateValues[19] = true;
                    break;
                case 'X':
                    fig.Score = r.ReadInt();
                    fig._updateValues[20] = true;
                    break;
                case 'Y':
                    fig.TeamNetID = (int)r.ReadUInt();
                    fig._updateValues[21] = true;
                    break;
                case '1':
                    fig.Speed = (int)r.ReadUInt();
                    fig._updateValues[22] = true;
                    break;
                case '2':
                    fig.JumpPower = (int)r.ReadUInt();
                    fig._updateValues[23] = true;
                    break;
                case '3':
                    fig.CameraFOV = (int)r.ReadUInt();
                    fig._updateValues[24] = true;
                    break;
                case '4':
                    fig.CameraDistance = r.ReadInt();
                    fig._updateValues[25] = true;
                    break;
                case '5':
                    fig.CameraPosition.x = r.ReadFloat();
                    fig._updateValues[26] = true;
                    break;
                case '6':
                    fig.CameraPosition.y = r.ReadFloat();
                    fig._updateValues[27] = true;
                    break;
                case '7':
                    fig.CameraPosition.z = r.ReadFloat();
                    fig._updateValues[28] = true;
                    break;
                case '8':
                    fig.CameraRotation.x = r.ReadFloat();
                    fig._updateValues[29] = true;
                    break;
                case '9':
                    fig.CameraRotation.y = r.ReadFloat();
                    fig._updateValues[30] = true;
                    break;
                case 'a':
                    fig.CameraRotation.z = r.ReadFloat();
                    fig._updateValues[31] = true;
                    break;
                case 'b':
                    fig.CameraType = r.ReadString();
                    fig._updateValues[32] = true;
                    break;
                case 'c':
                    fig.CameraNetID = (int)r.ReadUInt();
                    fig._updateValues[33] = true;
                    break;
                case 'e':
                    fig.Health = r.ReadFloat();
                    fig._updateValues[34] = true;
                    if (fig.Health > fig.MaxHealth) {
                        fig.MaxHealth = fig.Health;
                        fig._updateValues[35] = true;
                    }
                    break;
                case 'f':
                    fig.Speech = r.ReadString();
                    fig._updateValues[36] = true;
                    break;
                case 'g':
                    fig.EquippedToolSlotID = (int)r.ReadUInt();
                    fig._updateValues[37] = true;
                    fig.EquippedToolModel = (int)r.ReadUInt();
                    fig._updateValues[38] = true;

                    fig.Unequip = false;
                    fig._updateValues[39] = true;
                    break;
                case 'h':
                    fig.Unequip = true;
                    fig._updateValues[39] = true;
                    break;
            }
        }
        Player p = GetPlayer(netID);
        if (p == null) return;

        p.PlayerFigure.Combine(fig);

        if (Characters.ContainsKey(netID)) Characters[netID].GetComponent<CharacterMain>().UpdateFromFigure();

        bool isLocal = netID == localInfo.LocalPlayer.NetID;

        if (fig._updateValues[20]) {
            ui.UpdatePlayerScore(netID);
        }

        if (fig._updateValues[21]) {
            ui.AddPlayerToTeam(netID, fig.TeamNetID);
            if (!isLocal && Characters.ContainsKey(netID)) Characters[netID].GetComponent<CharacterMain>().UpdateNametag();

        }

        if (isLocal) {
            // it me
            ui.UpdateHealth(fig.Health / fig.MaxHealth);
        }
    }

    public void RemovePlayer (PacketReader r) {
        int netID = (int)r.ReadUInt();
        Playerlist.Remove(GetPlayer(netID));
        DeleteCharacter(netID);

        ui.UpdatePlayerlist();
    }

    public void CreateTool (PacketReader r) {
        Tool tool = new Tool {
            Action = r.ReadByte(),
            SlotID = (int)r.ReadUInt(),
            Name = r.ReadString(),
            Model = (int)r.ReadUInt()
        };

        if (tool.Action == 1) {
            ui.AddTool(tool);
        } else {
            ui.DeleteTool(tool.SlotID);
        }
    }

    public void CreateTeam (PacketReader r) {
        Team t = new Team();
        t.NetID = (int)r.ReadUInt();
        t.Name = r.ReadString();
        t.TeamColor = new Color(r.ReadByte()/255f, r.ReadByte()/255f, r.ReadByte()/255f, 1.0f);

        ui.AddTeamToPlayerlist(t);

        Teams.Add(t.NetID, t);
    }

    public void ModifyBot (PacketReader r) {
        int id = (int)r.ReadUInt();
        Bot bot = null;

        bool createBotChar = true;

        if (Bots.TryGetValue(id, out GameObject botGO)) {
            bot = botGO.GetComponent<BotCharacterMain>().bot;
            createBotChar = false;
        }

        if (bot == null) {
            bot = new Bot();
            bot.ID = id;
        }

        string botCode = r.ReadString();
        for (int i = 0; i < botCode.Length; i++) {
            switch (botCode[i]) {
                case 'A':
                    bot.Name = r.ReadString();
                    break;
                case 'B':
                    bot.Position.x = r.ReadFloat();
                    break;
                case 'C':
                    bot.Position.y = r.ReadFloat();
                    break;
                case 'D':
                    bot.Position.z = r.ReadFloat();
                    break;
                case 'E':
                    bot.Rotation.x = r.ReadFloat();
                    break;
                case 'F':
                    bot.Rotation.y = r.ReadFloat();
                    break;
                case 'G':
                    bot.Rotation.z = r.ReadFloat();
                    break;
                case 'H':
                    bot.Scale.x = r.ReadFloat();
                    break;
                case 'I':
                    bot.Scale.y = r.ReadFloat();
                    break;
                case 'J':
                    bot.Scale.z = r.ReadFloat();
                    break;
                case 'K':
                    bot.Colors.Head = Helper.DecToColor((int)r.ReadUInt());
                    break;
                case 'L':
                    bot.Colors.Torso = Helper.DecToColor((int)r.ReadUInt());
                    break;
                case 'M':
                    bot.Colors.LeftArm = Helper.DecToColor((int)r.ReadUInt());
                    break;
                case 'N':
                    bot.Colors.RightArm = Helper.DecToColor((int)r.ReadUInt());
                    break;
                case 'O':
                    bot.Colors.LeftLeg = Helper.DecToColor((int)r.ReadUInt());
                    break;
                case 'P':
                    bot.Colors.RightLeg = Helper.DecToColor((int)r.ReadUInt());
                    break;
                case 'Q':
                    bot.Face = (int)r.ReadUInt();
                    break;
                case 'U':
                    bot.HatID[0] = (int)r.ReadUInt();
                    break;
                case 'V':
                    bot.HatID[1] = (int)r.ReadUInt();
                    break;
                case 'W':
                    bot.HatID[2] = (int)r.ReadUInt();
                    break;
                case 'X':
                    bot.Speech = r.ReadString();
                    break;
            } 
        }

        if (createBotChar) CreateBotCharacter(bot);

        Bots[id].GetComponent<BotCharacterMain>().UpdateBot();
    }

    public void CreateBotCharacter (Bot bot) {
        if (Bots.ContainsKey(bot.ID)) {
            Debug.Log("attempted to create already existing bot");
            return; //dont
        }
        GameObject botGO = Instantiate(BotPrefab);
        botGO.name = bot.Name + " (BOT)";

        BotCharacterMain bcm = botGO.GetComponent<BotCharacterMain>();
        bcm.bot = bot;
        bcm.BillboardTarget = LocalCamera.transform;
        bcm.SetNametag(bot.Name, Color.white);

        Bots.Add(bot.ID, botGO);
    }

    public void ClearMap () {
        map.LoadedMap.Bricks.Clear(); // delete all bricks
        map.builder.RebuildEntireMap(); // generate map
    }

    public void SetDeathStatus (PacketReader r) {
        float net_id = r.ReadFloat(); // why float instead of uint?
        byte alive = r.ReadByte(); // why uint instead of byte?
        bool aliveBool = alive == 0;

        if (aliveBool) {
            // respawn player
            if (!Characters.ContainsKey((int)net_id)) {
                Player p = GetPlayer((int)net_id);
                CreateCharacter(p);
                p.PlayerFigure.Health = p.PlayerFigure.MaxHealth;
            }
        } else {
            // k i l l
            if (Characters.ContainsKey((int)net_id)) {
                Player p = GetPlayer((int)net_id);
                DeleteCharacter((int)net_id);
                p.PlayerFigure.Health = 0;
            }
        }

        if (net_id == localInfo.LocalPlayer.NetID) {
            // it me
            LocalCamera.GetComponent<CharacterCamera>().enabled = aliveBool;
            Player p = GetPlayer((int)net_id);
            ui.UpdateHealth(p.PlayerFigure.Health / p.PlayerFigure.MaxHealth);
        }

    }

    public void SetBrickProps (PacketReader r) {
        int brickID = (int)r.ReadUInt();
        string prop = r.ReadString();

        Map.Brick brick = map.LoadedMap.GetBrick(brickID);
        if (brick == null) {
            bool deleted = DeletedBricks.Contains(brickID);
            bool created = CreatedBricks.Contains(brickID);

            if (deleted) {
                //Debug.Log("Attempting to edit deleted brick! (" + brickID + ")");
            } else {
                //Debug.Log("Attempting to edit nonexistant brick! (" + brickID + ")");
            }
            return;
        }

        bool dontUpdate = false;

        switch (prop) {
            case "pos":
                brick.Position.x = r.ReadFloat();
                brick.Position.y = r.ReadFloat();
                brick.Position.z = r.ReadFloat();
                break;
            case "rot":
                brick.Rotation = (int)r.ReadUInt();
                break;
            case "roty":
                brick.RotationY = (int)r.ReadUInt();
                break;
            case "rotx":
                brick.RotationX = (int)r.ReadUInt();
                break;
            case "scale":
                brick.Scale.x = r.ReadFloat();
                brick.Scale.y = r.ReadFloat();
                brick.Scale.z = r.ReadFloat();
                break;
            case "kill":
                break;
            case "destroy":
                map.DestroyBrick(brick);
                dontUpdate = true;
                break;
            case "col":
                brick.BrickColor = new Color(r.ReadByte() / 255f, r.ReadByte() / 255f, r.ReadByte() / 255f, brick.BrickColor.a);
                break;
            case "alpha":
                brick.BrickColor = new Color(brick.BrickColor.r, brick.BrickColor.g, brick.BrickColor.b, r.ReadFloat());
                break;
            case "lightcol":
                break;
            case "lightrange":
                break;
            case "model":
                brick.Model = (int)r.ReadUInt();
                break;
            case "collide":
                brick.Collision = r.ReadByte() == 1;
                break;
            case "clickable":
                brick.Clickable = r.ReadByte() == 1;
                brick.ClickDistance = r.ReadUInt();
                // clickdist = uint32
                break;
        }

        if (!dontUpdate)
            map.UpdateBrick(brick);
    }

    // to networkmanager

    public void SendPacket (byte[] data) {
        net.SendData(data);
    }

    public void SendClickPacket (Map.Brick brick) {
        PacketBuilder b = new PacketBuilder((byte)NetworkManager.ClientPackets.Click);
        b.AppendUInt((uint)brick.ID);
        SendPacket(b.GetBytes());
    }

    // other

    public void SetGraphicsLevel (int level) {
        QualitySettings.SetQualityLevel(level);
    }

    public void SetGlobalVolume (float volume) {
        AudioListener.volume = volume;
    }

    public Player GetPlayer (int NetID) {
        for (int i = 0; i < Playerlist.Count; i++) {
            if (Playerlist[i].NetID == NetID) return Playerlist[i];
        }
        return null;
    }

    public void Quit () {
        Application.Quit();
    }
}
