using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

internal sealed class DemoAlbumView
{
    private const int GridColumns = 3;
    private readonly RectTransform root;
    private readonly List<Texture2D> textures = new();
    private RectTransform panel;
    private int version;
    private bool preparing;

    public DemoAlbumView(RectTransform root) => this.root = root;

    public async Task Open(Func<byte[], Task> send)
    {
        Close();
        int opening = version;
        var content = OpenPanel("Recent photos");
        var status = DemoUi.Label(content, "Loading photos…");
        try
        {
            string[] photos = await DemoAlbum.List();
            if (opening != version) return;
            status.text = photos.Length == 0 ? "No accessible photos." : "Choose a photo to preview it.";
            RectTransform row = null;
            for (int i = 0; i < photos.Length; i++)
            {
                string id = photos[i];
                byte[] bytes;
                try { bytes = await DemoAlbum.Read(id, DemoAlbum.ThumbnailSide); }
                catch (Exception) { bytes = null; }
                if (opening != version) return;
                if (i % GridColumns == 0) row = DemoUi.Row(content, "Album Row");
                var thumbnail = DemoUi.Button(row, "", () => Select(id, send, status));
                thumbnail.name = $"Photo {i + 1}";
                thumbnail.GetComponent<LayoutElement>().minHeight = 132;
                var texture = DemoGroupView.LoadPhoto(bytes);
                thumbnail.interactable = texture != null;
                if (texture == null) thumbnail.GetComponentInChildren<Text>().text = "Unavailable";
                else AddImage(thumbnail.GetComponent<RectTransform>(), texture);
            }
        }
        catch (Exception)
        {
            if (opening == version) status.text = "The photo library could not be opened.";
        }
    }

    private async void Select(string id, Func<byte[], Task> send, Text status)
    {
        if (preparing) return;
        preparing = true;
        int selection = version;
        status.text = "Preparing photo…";
        byte[] bytes;
        try { bytes = await DemoAlbum.Read(id, DemoGroup.MaxPhotoSide); }
        catch (Exception) { bytes = null; }
        if (selection != version) return;
        preparing = false;
        var texture = DemoGroupView.LoadPhoto(bytes);
        if (texture == null)
        {
            status.text = "This photo is not available. Try another photo stored on this device.";
            return;
        }

        Close();
        var content = OpenPanel("Share photo");
        var image = DemoUi.Rect(content, "Selected Photo");
        image.gameObject.AddComponent<LayoutElement>().preferredHeight = 360;
        AddImage(image, texture);
        DemoUi.Button(content, "Send photo", async () =>
        {
            Close();
            await send(bytes);
        }, true);
    }

    private RectTransform OpenPanel(string title)
    {
        panel = DemoUi.Rect(root, "Photo Album");
        panel.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        DemoUi.Fill(panel);
        DemoUi.Surface(panel, DemoUi.Background, true);
        DemoUi.Stack(panel, 24);
        var header = DemoUi.Row(panel, "Album Header");
        DemoUi.Label(header, title, 32, true);
        DemoUi.Button(header, "Close", Close);
        var content = DemoUi.Scroll(panel, "Photo List", out var scroll);
        scroll.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        return content;
    }

    private void AddImage(RectTransform parent, Texture2D texture)
    {
        textures.Add(texture);
        DemoGroupView.PhotoSurface(parent, texture, 6);
    }

    public void Close()
    {
        version++;
        preparing = false;
        if (panel != null)
        {
            panel.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(panel.gameObject);
            panel = null;
        }
        foreach (var texture in textures) UnityEngine.Object.Destroy(texture);
        textures.Clear();
    }
}
