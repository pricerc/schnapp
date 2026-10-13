# Persistent SSH material (survive system image rebuild)

There are **two different** SSH key types. Your error is almost always about **host** keys, not login keys.

| Kind | Who has it | Symptom when it changes |
|------|------------|-------------------------|
| **Host keys** | Server identity (`/etc/ssh/ssh_host_*`) | `REMOTE HOST IDENTIFICATION HAS CHANGED` / known_hosts |
| **Authorized keys** | Your laptop → root/schnapp login | Password prompt or `Permission denied (publickey)` |

---

## 1. Host keys (server fingerprint — fix “identification has changed”)

Generate **once** on the build machine, then every rebuild embeds the same keys:

```bash
cd scripts/appliance/ssh
mkdir -p hostkeys
ssh-keygen -t ed25519 -f hostkeys/ssh_host_ed25519_key -N '' -C 'schnapp-appliance-host'
ssh-keygen -t rsa -b 3072 -f hostkeys/ssh_host_rsa_key -N '' -C 'schnapp-appliance-host'
# optional ECDSA:
# ssh-keygen -t ecdsa -b 256 -f hostkeys/ssh_host_ecdsa_key -N '' -C 'schnapp-appliance-host'
```

Build then installs them as `/etc/ssh/ssh_host_*` (disables sshd auto-regenerate on first boot).

**Gitignored** (private keys must not be committed). Keep a backup of `hostkeys/`.

After the first rebuild that uses fixed host keys, clear the old known_hosts entry **once**:

```bash
ssh-keygen -R schnapp
ssh-keygen -R 10.10.10.55
ssh root@schnapp   # accept the new stable fingerprint once
```

---

## 2. Authorized keys (passwordless login as root / schnapp)

| File | Installed as |
|------|----------------|
| `authorized_keys.root` | `/root/.ssh/authorized_keys` |
| `authorized_keys.schnapp` | `/home/schnapp/.ssh/authorized_keys` |

```bash
cat ~/.ssh/id_ed25519.pub >> scripts/appliance/ssh/authorized_keys.root
cat ~/.ssh/id_ed25519.pub >> scripts/appliance/ssh/authorized_keys.schnapp
```

Also gitignored. Override directory: `SCHNAPP_SSH_KEYS_DIR=/path/to/dir`.

---

## Layout

```text
scripts/appliance/ssh/
  authorized_keys.root          # your public keys (login)
  authorized_keys.schnapp
  hostkeys/
    ssh_host_ed25519_key        # private host key
    ssh_host_ed25519_key.pub
    ssh_host_rsa_key
    ssh_host_rsa_key.pub
  *.example                     # templates (tracked)
```
