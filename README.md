# Cmds Manager

Compact portable manager for CMD, BAT, PowerShell, and VBS scripts on Windows.

Current version: **1.0.0**

Cmds Manager lives in the notification area and provides one place to organize,
start, monitor, and stop scripts. It supports Windows PowerShell 5.1 and
PowerShell 7, managed child console tabs, output encoding correction, detachable
and full-screen consoles, per-script auto-start, application auto-start, themes,
and INI-based configuration.

## Features

- Add, edit, remove, start, and stop `.cmd`, `.bat`, `.ps1`, and `.vbs` entries
- Stop the complete managed process tree when a script, tab, or application closes
- Capture output in modern console tabs, including managed child CMD launches
- Detach a running console without restarting its process
- Select UTF-8, OEM Windows, Windows-1251, UTF-16 LE, or automatic decoding
- Copy or save selected text and save the complete console buffer
- Configure console fonts, colors, opacity, Word Wrap, themes, and workspace size
- Start selected scripts with the application and start the application with Windows
- Use Russian or English UI strings stored in `CmdsManager.ini`
- Run as a small Portable ZIP without elevation or bundled runtime dependencies

## Requirements

- Windows 10 or Windows 11 x64
- .NET Framework 4.8
- PowerShell 7 only for entries that explicitly select it

## Download and documentation

The release is distributed as a Portable ZIP. Extract the complete archive to a
writable folder and run `CmdsManager.exe`.

The bilingual English/Russian user guide, version history, and complete INI
reference are in [Readme.txt](Readme.txt).

## Support the Project

If Cmds Manager saves you time, you can support the author:

- Russia transfers, Ozon Bank / SBP: [open payment page](https://finance.ozon.ru/apps/sbp/ozonbankpay/019a0a87-1f4a-7df8-97c7-ef32ebf9a0e3)
- VISA: `4400430236422744`
- [Buy Me a Coffee](https://buymeacoffee.com/danceworldtv)
- [PayPal](https://paypal.me/imiked)
- [Boosty](https://boosty.to/danceworldtv/donate)
- USDT Ethereum (ERC20): `0xBd0593dDF1DFC7fD95bB6F4e6A5c73Da44048B40`
- USDT TON: `UQCr2Fp7t34QFuO4IesN3Lo3186a93Z1B7Wu76imr6APIXgk`
- USDT Tron (TRC20): `TE5A3GT84eJ9iT3mYYLv1KXJnMaiZFxNuA`

## Author

[iMiKED from 4PDA](https://4pda.to/forum/index.php?showuser=1017942)

## License

Cmds Manager is free software distributed under the
[GNU General Public License v3.0](https://www.gnu.org/licenses/gpl-3.0.html).
