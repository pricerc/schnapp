Schnapp client kits
===================

This SMB share holds install/upgrade kits for workstation tray clients.
Connect from another machine:

  Windows:  \\<schnapp-vm-ip>\clients
  Linux:    smb://<schnapp-vm-ip>/clients
            or: mount -t cifs //<ip>/clients /mnt -o guest

Layout (when built into the image)
----------------------------------

  linux/          Linux tray client publish kit (deploy-linux-client.sh, app/, …)
  windows/        Windows tray client kit (when supplied at appliance build time)
  CLIENTS.txt     What was included in this image build

Server API base URL for client login is this VM, e.g.:

  http://<schnapp-vm-ip>:5080

or HTTPS if you terminate TLS on the QNAP reverse proxy in front of the VM.

See docs/client.md in the Schnapp repository for capture/config details.
