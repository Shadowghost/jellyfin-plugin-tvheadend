<h1 align="center">Jellyfin TVHeadend Plugin</h1>
<h3 align="center">Part of the <a href="https://jellyfin.org">Jellyfin Project</a></h3>

<p align="center">
<img alt="Plugin Banner" src="https://raw.githubusercontent.com/jellyfin/jellyfin-ux/master/plugins/SVG/jellyfin-plugin-tvheadend.svg?sanitize=true"/>
<br/>
<br/>
<a href="https://github.com/jellyfin/jellyfin-plugin-tvheadend/actions/workflows/build.yaml">
<img alt="GitHub Workflow Status" src="https://img.shields.io/github/actions/workflow/status/jellyfin/jellyfin-plugin-tvheadend/build.yaml?branch=master"/>
</a>
<a href="https://github.com/jellyfin/jellyfin-plugin-tvheadend">
<img alt="MIT License" src="https://img.shields.io/github/license/jellyfin/jellyfin-plugin-tvheadend.svg"/>
</a>
<a href="https://github.com/jellyfin/jellyfin-plugin-tvheadend/releases">
<img alt="Current Release" src="https://img.shields.io/github/release/jellyfin/jellyfin-plugin-tvheadend.svg"/>
</a>
</p>

## About

This plugin allows you to manage TVHeadend from Jellyfin.

## Installation

[See the official documentation for install instructions](https://jellyfin.org/docs/general/server/plugins/index.html#installing).

## Usage

The plugin registers itself as a Live TV service, so there is no tuner or guide provider to add under *Dashboard → Live TV*.

1. In TVHeadend, make sure there is a user with a password that is allowed to stream and to use HTSP. Channels have to exist and be mapped to services, otherwise everything below succeeds and Live TV still stays empty.
2. In Jellyfin, open *Dashboard → Plugins → TVHeadend* and fill in the hostname, the HTTP port (9981 by default), the HTSP port (9982 by default) and those credentials. TVHeadend behind a path prefix needs no extra setting: the plugin adopts the web root the server reports during the handshake.
3. Press **Save and test connection**. It saves the form and then has the server check its own saved settings - the page only asks for the check, the credentials never travel back to it. It reports both endpoints the plugin needs:
   - *HTSP* - the channel, guide and recording connection: server name, version, the negotiated protocol version, or the reason the handshake or the login failed.
   - *HTTP* - the endpoint streams and recordings are fetched from: whether the credentials are accepted, which authentication scheme TVHeadend asks for, and how many channels this user may watch.
4. Restart Jellyfin. The connection settings are read once per server run, so a changed host, port or account only takes effect after a restart.
5. Verify in the UI: *Live TV* lists the channels and their guide data, and existing TVHeadend recordings show up in the *TVHeadEnd Recordings* channel unless that channel is hidden in the settings. A recording created from the guide appears as an upcoming recording in TVHeadend's *Digital Video Recorder* tab.

If Live TV stays empty, the server log is the next place to look: every message from this plugin is prefixed with `[TVHclient]`, and connection problems are logged by `HTSConnectionHandler`.

## Build

1. To build this plugin you will need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

2. Build the plugin with the following command:

  ```sh
  dotnet publish TVHeadEnd/TVHeadEnd.csproj --configuration Release --output bin
  ```

3. Place `bin/TVHeadEnd.dll` in the `plugins/tvheadend` folder (you might need to create the folders) of your Jellyfin install.

## Development

The build enforces the shared Jellyfin analyzer set (StyleCop, .NET code analysis, SerilogAnalyzer and MultithreadingAnalyzer) with `TreatWarningsAsErrors`, so warnings fail the build.
Before pushing, check formatting and analyzer compliance:

```sh
dotnet format TVHeadEnd.slnx --verify-no-changes
dotnet build TVHeadEnd.slnx -c Release
```

`dotnet format TVHeadEnd.slnx` (without the flag) applies the fixes it can automatically.

## Releasing

To release the plugin we recommend [JPRM](https://github.com/oddstr13/jellyfin-plugin-repository-manager) that will build and package the plugin.
For additional context and for how to add the packaged plugin zip to a plugin manifest see the [JPRM documentation](https://github.com/oddstr13/jellyfin-plugin-repository-manager) for more info.

## Contributing

We welcome all contributions and pull requests! If you have a larger feature in mind please open an issue so we can discuss the implementation before you start.
In general refer to our [contributing guidelines](https://github.com/jellyfin/.github/blob/master/CONTRIBUTING.md) for further information.

## Licence

This plugins code and packages are distributed under the MIT License. See [LICENSE](./LICENSE) for more information.
