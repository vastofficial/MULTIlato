<div align="center">
   <img width="125" src="logo.png" alt="Logo">
</div>

<div align="center">
  <h1><b>MULTIlato</b></h1>
  <p><i>Jellyfin Stremio Integration Plugin — multiple manifests, one plugin</i></p>
</div>

MULTIlato is a fork of [Gelato](https://github.com/lostb1t/Gelato) that adds support for **multiple AIOStreams manifests**, each mapped to its own movie and series library. Where Gelato configures a single manifest/URL, MULTIlato lets you define any number of named instances (e.g. "Main", "Kids", "4K"), each with its own AIOStreams source and its own filesystem paths. Search and catalog import fan out across every enabled instance.

Bring the power of Stremio addons directly into Jellyfin. This plugin replaces Jellyfin's default search with Stremio-powered results and can automatically import entire catalogs into your library through scheduled tasks, seamlessly injecting them into Jellyfin's database so they behave like native items.

### Features
- **Multiple Instances** – Configure any number of independent AIOStreams manifests, each with its own movie/series library
- **Unified Search** – Jellyfin search pulls results from every enabled instance
- **Catalogs** – Import items from Stremio catalogs into your library with scheduled tasks, per instance
- **Realtime Streaming** – Streams are resolved on demand and play instantly
- **Database Integration** – Stremio items appear like native Jellyfin items
- **Act as a proxy** - Streams are proxied through Jellyfin, so debrid sees everything as a single IP
- **Per user settings** - Users can have their own manifest override, perfect for age restricted accounts
- **More Content, Less Hassle** – Expand Jellyfin with community-driven Stremio catalogs

## Usage

1. Setup one or more aiostreams manifests. You can selfhost or use a public instance, for example: [Elfhosted public instance](https://aiostreams.elfhosted.com/stremio/configure)

   At minimum you need the **tmdb addon enabled** for search and one addon that provides streams (comet for example).
   Alternatively you can import the [starter config](aiostreams-config.json). Remember to enable your debrid providers under services after importing the config.

2. Make sure you are running Jellyfin 10.11 and add `https://raw.githubusercontent.com/vastofficial/MULTIlato/refs/heads/gh-pages/repository.json` to your plugin repositories.

3. Install and configure the plugin.
   **Note:** Only **AIOStreams** is supported.

4. In the plugin's **Instances** tab, add one instance per manifest with its own movie/series paths. Add each instance's paths to a Jellyfin library, then start a library scan.

5. For shows, enable the "Gelato missing season/episode fetcher" and put it on top of the metadata downloaders.

6. Profit! Now search for your favorite movie and start streaming across every configured instance. Or run the catalog import task to populate your db.

For a more in depth guide on the base plugin see the original [starter guide](https://github.com/lostb1t/Gelato/discussions/40).

## Notes

- Only **AIOStreams** is supported
- A single-manifest Gelato config is automatically migrated into instance #1 on first load

### FAQ

- You need to restart the server after editing the manifest/config in aiostreams.
- You should have at least one search enabled catalog per instance. I suggest the tmdb addon.
- if something borked or you want to start over, you can use the purge task under scheduled tasks.
- I suggest lowering the default timeout on your stremio addons in aiostreams (5 seconds for example)
- debridio tmdb and debridio tvdb are problematic. I suggest using the regular tmdb addon.
- Stream cache can be cleared by restarting the server

## Credits

MULTIlato is a fork of [lostb1t/Gelato](https://github.com/lostb1t/Gelato), licensed under [GPL-3.0](LICENSE). All credit for the original plugin design and implementation goes to the Gelato project and its contributors.

### ❤️ Support the Project

- ⭐ **[Star the original Gelato repository](https://github.com/lostb1t/Gelato)** on GitHub.
- 🤝 **Contribute**: Report issues, suggest features, or submit pull requests.
