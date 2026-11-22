using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

internal sealed class DemoGroupView
{
    private static readonly Color[] BlockColors =
    {
        new Color32(73, 201, 178, 255), new Color32(247, 164, 101, 255),
        new Color32(127, 167, 250, 255), new Color32(227, 127, 170, 255),
        new Color32(219, 204, 109, 255)
    };

    private readonly RectTransform lobby;
    private readonly RectTransform roster;
    private readonly RectTransform messages;
    private readonly RectTransform scene;
    private readonly RectTransform board;
    private readonly RectTransform overlayRoot;
    private readonly Text lobbyHint;
    private readonly Text sceneHint;
    private readonly Button ready;
    private readonly Button start;
    private readonly InputField input;
    private readonly Button send;
    private readonly Button color;
    private readonly Button[] cells = new Button[DemoGroup.BoardSize * DemoGroup.BoardSize];
    private readonly Dictionary<DemoChat, (GameObject row, Texture2D texture)> rows = new();
    private RectTransform preview;
    private int chatVersion = -1;

    public DemoGroupView(Transform parent, RectTransform overlayRoot, Action toggleReady, Action startScene,
        Action<string> sendText, Action<int> move, Action cycleColor)
    {
        this.overlayRoot = overlayRoot;
        lobby = DemoUi.Rect(parent, "Lobby");
        DemoUi.Stack(lobby, 18);
        DemoUi.Surface(lobby, DemoUi.Panel);
        lobbyHint = DemoUi.Label(lobby, "", 24);
        lobbyHint.color = DemoUi.Muted;
        roster = DemoUi.Rect(lobby, "Participants");
        DemoUi.Stack(roster);
        var actions = DemoUi.Row(lobby, "Lobby Actions");
        ready = DemoUi.Button(actions, "Ready", toggleReady);
        start = DemoUi.Button(actions, "Start scene", startScene, true);
        DemoUi.Label(lobby, "Group chat", 32, true);
        input = DemoUi.Input(lobby, "Message", DemoGroup.MaxText);
        send = DemoUi.Button(lobby, "Send message", () =>
        {
            if (string.IsNullOrWhiteSpace(input.text)) return;
            sendText(input.text);
            input.text = "";
        }, true);
        messages = DemoUi.Rect(lobby, "Chat Messages");
        DemoUi.Stack(messages);

        scene = DemoUi.Rect(parent, "Shared Scene");
        DemoUi.Stack(scene, 18);
        DemoUi.Surface(scene, DemoUi.Panel);
        sceneHint = DemoUi.Label(scene, "", 24);
        sceneHint.color = DemoUi.Muted;
        board = DemoUi.Rect(scene, "Shared Board");
        DemoUi.Stack(board);
        for (int y = 0; y < DemoGroup.BoardSize; y++)
        {
            var row = DemoUi.Row(board, $"Row {y + 1}");
            for (int x = 0; x < DemoGroup.BoardSize; x++)
            {
                int cell = y * DemoGroup.BoardSize + x;
                cells[cell] = DemoUi.Button(row, "", () => move(cell));
                cells[cell].name = $"Cell {x + 1}, {y + 1}";
            }
        }
        color = DemoUi.Button(scene, "Change block color", cycleColor, true);
    }

    public void Render(DemoTab tab, DemoGroup group, bool host)
    {
        lobby.gameObject.SetActive(tab == DemoTab.Lobby);
        scene.gameObject.SetActive(tab == DemoTab.SharedScene);
        if (tab == DemoTab.Echo) return;

        if (tab == DemoTab.Lobby)
        {
            lobbyHint.text = group.Joined ? $"Participants  ·  {group.Members.Length}"
                : "Host a room or join a nearby host to chat.";
            DemoUi.Clear(roster);
            foreach (var member in group.Members)
            {
                string role = member.Id == 0 ? " (host)" : "";
                DemoUi.Label(roster, $"{member.Name}{role}  ·  {(member.Ready ? "Ready" : "Waiting")}", 24);
            }

            ready.gameObject.SetActive(group.Joined && !group.InScene);
            ready.GetComponentInChildren<Text>().text = group.Ready ? "Cancel ready" : "Ready";
            start.gameObject.SetActive(group.Joined && host && !group.InScene);
            start.interactable = group.CanStart;
            input.interactable = group.Joined;
            send.interactable = group.Joined;
            if (chatVersion != group.ChatVersion)
            {
                RenderChat(group.Chat);
                chatVersion = group.ChatVersion;
            }
        }
        else
        {
            sceneHint.text = group.InScene ? "Tap a tile to move the shared block." : "Start the shared scene from the lobby.";
            board.gameObject.SetActive(group.InScene);
            color.gameObject.SetActive(group.InScene);
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i].targetGraphic.color = i == group.Cell ? BlockColors[group.Color] : DemoUi.ButtonColor;
                cells[i].GetComponentInChildren<Text>().text = i == group.Cell ? "■" : "";
            }
        }
    }

    private void RenderChat(IReadOnlyList<DemoChat> chat)
    {
        var current = new HashSet<DemoChat>(chat);
        var expired = new List<DemoChat>();
        foreach (var pair in rows)
        {
            if (current.Contains(pair.Key)) continue;
            pair.Value.row.SetActive(false);
            UnityEngine.Object.Destroy(pair.Value.row);
            if (pair.Value.texture != null) UnityEngine.Object.Destroy(pair.Value.texture);
            expired.Add(pair.Key);
        }
        foreach (var message in expired) rows.Remove(message);

        foreach (var message in chat)
        {
            if (rows.ContainsKey(message)) continue;
            var row = DemoUi.Rect(messages, "Chat Message");
            row.SetAsFirstSibling();
            DemoUi.Stack(row);
            DemoUi.Label(row, message.Sender, 24, true).color = DemoUi.Muted;
            Texture2D texture = null;
            if (message.Photo == null) DemoUi.Label(row, message.Text);
            else
            {
                texture = LoadPhoto(message.Photo);
                if (texture == null) DemoUi.Label(row, "Image could not be opened.");
                else
                {
                    var thumb = DemoUi.Rect(row, "Image Thumbnail");
                    thumb.gameObject.AddComponent<LayoutElement>().preferredHeight = 168;
                    PhotoSurface(thumb, texture, 4);
                    DemoUi.Button(row, "Open photo", () => ShowPhoto(message.Photo));
                }
            }
            rows.Add(message, (row.gameObject, texture));
        }
    }

    public void Reset()
    {
        RenderChat(Array.Empty<DemoChat>());
        input.text = "";
        ClosePreview();
    }

    public void ShowPhoto(byte[] bytes)
    {
        var texture = LoadPhoto(bytes);
        if (texture == null) return;
        ClosePreview();
        preview = DemoUi.Rect(overlayRoot, "Photo Preview");
        preview.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        DemoUi.Fill(preview);
        DemoUi.Surface(preview, DemoUi.Background, true);
        DemoUi.Stack(preview, 24);
        DemoUi.Button(preview, "Close photo", ClosePreview);
        var image = DemoUi.Rect(preview, "Photo");
        image.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        PhotoSurface(image, texture, 4);
        previewTexture = texture;
    }

    private Texture2D previewTexture;

    private void ClosePreview()
    {
        if (preview == null) return;
        preview.gameObject.SetActive(false);
        UnityEngine.Object.Destroy(preview.gameObject);
        UnityEngine.Object.Destroy(previewTexture);
        preview = null;
        previewTexture = null;
    }

    private static Texture2D LoadPhoto(byte[] bytes)
    {
        if (!DemoPhotoBytes.Valid(bytes)) return null;
        var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        if (texture.LoadImage(bytes, true)) return texture;
        UnityEngine.Object.Destroy(texture);
        return null;
    }

    private static void PhotoSurface(RectTransform frame, Texture2D texture, float inset)
    {
        var image = DemoUi.Rect(frame, "Image").gameObject.AddComponent<RawImage>();
        DemoUi.Fill(image.rectTransform, inset);
        image.raycastTarget = false;
        image.texture = texture;
        var aspect = image.gameObject.AddComponent<AspectRatioFitter>();
        aspect.aspectRatio = (float)texture.width / texture.height;
        aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
    }
}
