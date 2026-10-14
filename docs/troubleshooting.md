# Troubleshooting

## Missing .NET SDK

If `dotnet` is missing, install .NET SDK 10 and rerun:

```bash
dotnet --info
```

## Shell Will Not Start

Check:

- `dotnet restore`
- `dotnet build`
- Avalonia package restore output
- Graphics drivers and desktop session logs

## ISO Tools Missing

Install Arch `archiso` for `mkarchiso` and QEMU for VM testing.

