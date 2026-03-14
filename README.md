# 
       /\
    __//\\__
  /          \
 |  [ (O) ]  |
  \__      __/
     \\  //
      \//
       \/

# Venom NetGuard

**Venom NetGuard** is a tactical, lightweight Windows Security Event monitor. It provides passive, stealthy background monitoring of your local network interface, triggering alerts only when specific threat thresholds are met.

## Overview
Unlike active firewalls, Venom NetGuard acts as a silent sentinel. It hooks directly into the Windows Event Log to detect unauthorized access attempts and network share anomalies in real-time, keeping you informed via a clean, distraction-free dashboard and tray notifications.

## Key Features
* **Passive Event Monitoring:** Listens silently to the `Security` event log.
* **Targeted Detection:** * Tracks Failed Logons (`Event ID 4625`).
  * Tracks Network Share Access (`Event ID 5140`).
* **Stealth Auto-Start:** Uses Windows Task Scheduler to run seamlessly on boot with high privileges, bypassing annoying UAC prompts.
* **Alert Throttling:** Smart tray notifications prevent notification spam during automated attacks (e.g., port scans).

## Screenshots
![Dashboard](venom1.png "Venom NetGuard Dashboard")

## Installation & Usage
1. Clone the repository and open `.sln` in Visual Studio.
2. Build the project.
3. **Important:** Run the application as an **Administrator**. 
   *(Admin privileges are strictly required to read the Windows Security Log and to register the auto-start task).*

## Notes for Developers
- The project uses `System.Text.Json` to save local user preferences. 
- Local settings and intercepted logs are saved to `nexus_data.json` (ignored in `.gitignore` to prevent leaking local data to the repository).
- The `private/` directory is reserved for local, uncommitted experimental modules (e.g., future packet inspection tools).

## Disclaimer
This tool is built for educational purposes, personal network monitoring, and system administration. Always respect local laws and network policies.