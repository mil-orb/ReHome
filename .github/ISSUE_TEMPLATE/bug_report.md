---
name: Bug report
about: Something went wrong during a scan, export or install
labels: bug
---

**What happened**
What you did, what you expected, and what Rehome did instead. Include the exact message if there was one.

**Which side**
- [ ] Old PC (scan or export)
- [ ] New PC (opening an export, or installing)

**Versions**
- Rehome: (About in the app, or `rehome --version`)
- Windows: (run `winver`, e.g. 11 24H2 build 26100.4061)
- App Installer / winget: (run `winget --version`)

**Log**
Attach `%LOCALAPPDATA%\Rehome\rehome.log`, or paste the lines around the failure. It lists app names and package ids from the scan, so trim anything you don't want public. It never holds settings file contents.

**Which app, if it's about one**
Name and the winget or Store id Rehome showed for it.
