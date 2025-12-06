# Crystal Radio

A Dalamud plugin for FINAL FANTASY XIV that lets you stream internet radio stations directly in-game.

## Features

- 🎵 **Stream Internet Radio** - Listen to thousands of internet radio stations while playing FFXIV
- 🔍 **Search & Browse** - Search for stations
- ⭐ **Favorites System** - Save your favorite stations for quick access
- 🎚️ **Volume Control** - Adjust radio volume independently from game audio
- 📻 **ICY Metadata Support** - See currently playing track information when supported by the station
- 🎮 **Seamless Integration** - Simple in-game UI that doesn't interrupt your gameplay
- ⏯️ **Playback Controls** - Play, pause, stop, and switch between stations with ease

## Installation

### From Dalamud Plugin Repository (Recommended)

1. Open FINAL FANTASY XIV with XIVLauncher
2. Type `/xlplugins` in chat to open the Plugin Installer
3. Search for "Crystal Radio"
4. Click "Install"

## Usage

### Basic Commands

- `/cradio` - Opens the Crystal Radio player window

### Using the Radio Player

1. Open the player with `/cradio`
2. Browse or search for stations in the list
3. Click on a station to select it
4. Use the playback controls to:
   - **Play** - Start streaming the selected station
   - **Pause** - Pause the current stream
   - **Stop** - Stop playback completely
5. Adjust volume using the slider
6. Add stations to your favorites by clicking the star icon

### Favorites

- Click the star icon next to any station to add it to your favorites
- Access your favorites from the "Favorites" tab for quick selection
- Remove favorites by clicking the star icon again

## Technical Details

### Architecture

- **Audio Engine**: NAudio library for audio streaming
- **Metadata**: ICY metadata protocol support for track information
- **UI**: ImGui-based interface via Dalamud
- **Radio Stations**: Sourced from public radio station APIs

### Key Components

- `RadioController` - Manages playback state and station control
- `AudioPlayer` - Handles audio streaming using NAudio
- `IcyMetadataService` - Parses and provides track metadata
- `MainWindow` - Primary UI for station browsing and playback
- `ConfigWindow` - Settings and configuration interface

## Contributing

Contributions are welcome! Please feel free to submit pull requests or open issues for bugs and feature requests.

### Areas for Contribution

- Additional radio station sources/APIs
- UI/UX improvements
- Performance optimizations
- Bug fixes
- Documentation improvements

## License

This project is licensed under the GNU Affero General Public License v3.0 - see the [LICENSE.md](LICENSE.md) file for details.

## Acknowledgments

- Built for [Dalamud](https://github.com/goatcorp/Dalamud)
- Audio streaming powered by [NAudio](https://github.com/naudio/NAudio)
- Thanks to the FFXIV modding community

## Support

If you encounter any issues or have questions:
- Open an issue on GitHub
- Join the [Dalamud Discord](https://discord.gg/holdshift) for general plugin support

---

**Note**: This plugin requires an active internet connection to stream radio stations. Stream quality depends on your connection speed and the radio station's server.
