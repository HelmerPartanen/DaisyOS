You will only need to download the repository, install a few components, and select DaisyOS from your login screen.

## 1. Requirements

Before starting, make sure you have:

- An existing Arch Linux installation.
- A working graphical login screen.
- An internet connection.
- Administrator privileges.
- Enough free storage for the repository and its dependencies.

Arch Linux is recommended because DaisyOS is developed and tested primarily against Arch Linux and its KDE/KWin components.

**Important:** This guide installs DaisyOS as a desktop session on an existing Linux installation. It does not replace your current operating system or install a separate DaisyOS partition.

## 2. Download DaisyOS

First, open a terminal.

Install Git if you don’t already have it:

```bash
sudo pacman -S --needed git
```

Download the DaisyOS repository:

```bash
git clone https://github.com/HelmerPartanen/DaisyOS.git
```

Enter the downloaded folder:

```bash
cd DaisyOS
```

You now have everything needed to install the DaisyOS native development session.

Keep this folder on your computer. DaisyOS uses it to locate its shell and supporting components.

## 3. Install DaisyOS

Run the native session installer:

```bash
sudo ./scripts/install-native-development-session.sh \
  --repo "$PWD"
```

This registers DaisyOS with your graphical login manager and prepares the system for running the desktop.

The installer handles several supporting components and configurations, including:

- DaisyOS Native Development login session.
- Memory resilience configuration.
- Gaming integration.
- Desktop services and portal configuration.
- Bluetooth support and related integrations.
- Supporting system configuration.

It also attempts to configure the required LAN firewall access when firewalld is installed and running.

The installation may take some time because additional components can be downloaded or built.

### Install the DaisyOS window decorations

DaisyOS includes its own custom window decorations, giving application windows a consistent appearance.

Install them using:

```bash
sudo ./scripts/install-kwin-window-decoration.sh
```

The KWin frame effect is installed automatically for your user during the first native session startup when required.

## 4. Boot into DaisyOS

Once installation is complete:

1. Save any open work.
2. Log out of your current desktop.
3. Open the session selector on your login screen.
4. Select **DaisyOS (Native Development)**.
5. Log in normally.

\<Insert screenshot of the graphical login screen with DaisyOS selected here.>

DaisyOS should now start as your desktop environment.

You should see the DaisyOS desktop, dock, system interface, and available desktop widgets.

**Congratulations! You’re now running DaisyOS.**

You can still return to your previous desktop environment whenever you want by logging out and selecting it again.

## 5. Customize your desktop

DaisyOS is designed to be customizable without requiring users to edit configuration files or write code.

Start by opening the DaisyOS Settings application.

Explore the available appearance and desktop settings, including:

- Light and dark appearance.
- Accent colors and interface styling.
- Desktop and wallpaper settings.
- Display configuration.
- Audio devices.
- Network and Bluetooth settings.
- Available desktop and widget configurations.

\<Insert screenshot of DaisyOS Settings here.>

### Customize your desktop widgets

DaisyOS includes interactive desktop widgets.

Depending on the available features in your build, you can explore:

- Different clock styles.
- Calendar widgets.
- Media visualizations.
- Photo frames.
- Widget gallery and desktop layout.

\<Insert screenshot of the DaisyOS desktop widget gallery here.>

### Customize your wallpaper

Choose a wallpaper that suits your desktop.

Experiment with different backgrounds and desktop arrangements to create your preferred look.

## 6. Connect your other devices with Continuity

One of DaisyOS’s most interesting features is Continuity.

Continuity lets multiple DaisyOS computers work together over your local network.

For example, you can use your desktop computer and laptop as one connected workspace.

Features include:

- **Seamless mouse and keyboard:** Move your mouse beyond the edge of one screen to control another computer.
- **Shared clipboard:** Copy something on one computer and paste it on another.
- **File transfers:** Send files directly between paired computers.
- **Audio routing:** Use another DaisyOS computer as an audio output.
- **App handoff:** Continue supported work on another device.
- **Screen viewing:** View or control another device through supported remote sessions.

### Set up Continuity

First, make sure both computers have DaisyOS installed and are connected to the same local network.

Build and install the Continuity service on each computer:

```bash
cd ~/DaisyOS

./scripts/build-continuity.sh
./scripts/install-continuity.sh
```

Start the service:

```bash
systemctl --user daemon-reload

systemctl --user enable --now daisyos-continuity.service
```

Repeat these steps on the other computer.

Open DaisyOS Settings and navigate to Continuity.

Discover the other computer, complete the pairing process, and enable the features you want to use.

Once paired, the computers can communicate directly over the local network without requiring a cloud account.

For seamless input, configure the screen arrangement and move your mouse across the configured screen edge.

If you ever need to return control to your local computer immediately, use:

`Ctrl + Alt + Shift + Escape`

## 7. Keep DaisyOS up to date

Because this installation uses the Git repository, you can download the latest changes whenever you want.

Open a terminal:

```bash
cd ~/DaisyOS
```

Download the latest repository changes:

```bash
git pull --ff-only
```

After updating, rebuild and restart the desktop:

```bash
daisyos-native-development restart
```

This rebuilds the shell and standalone applications and restarts the DaisyOS desktop without requiring you to log out.

Some larger system updates, such as changes to native KWin plugins, may require additional installation steps or a new login session.

**Tip:** If you have made local modifications to the repository, make sure to preserve them before updating.

## 8. Returning to your previous desktop

DaisyOS does not permanently replace your existing desktop session.

To return to your previous environment:

1. Open the system menu.
2. Log out of DaisyOS.
3. Select your previous desktop environment at the login screen.
4. Log in normally.

Your original Linux installation and applications remain available.

## 9. Something not working?

If DaisyOS doesn’t start correctly, first try logging into your previous desktop environment.

You can inspect the native session logs using:

```bash
daisyos-native-development logs
```

Check whether the session is running:

```bash
daisyos-native-development status
```

For more help, visit the DaisyOS GitHub repository and open an issue:

[https://github\\.com/HelmerPartanen/DaisyOS/issues](https://github\\.com/HelmerPartanen/DaisyOS/issues)

When reporting a problem, include your hardware information, Linux distribution, and relevant error messages.
