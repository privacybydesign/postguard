# <p align="center"><img src="../img/pg_logo.svg" height="128px" alt="PostGuard" /></p>

> For full documentation, visit [docs.postguard.eu](https://docs.postguard.eu/repos/postguard-dotnet).

.NET SDK for PostGuard encryption, published as `E4A.PostGuard` on NuGet. Allows .NET applications to encrypt files for recipients using identity-based encryption. This is a sending-side SDK: it handles encryption and upload, while decryption is handled by the recipient through [postguard.eu](https://postguard.eu) or the mail plugins.

## Quick Start

```csharp
using E4A.PostGuard;
using E4A.PostGuard.Models;

var pg = new PostGuard(new PostGuardConfig
{
    PkgUrl = "https://pkg.postguard.eu",
    CryptifyUrl = "https://fileshare.postguard.eu"
});

var encrypted = pg.Encrypt(new EncryptInput
{
    Files = [new PgFile("report.txt", fileStream)],
    Recipients = [pg.Recipient.Email("citizen@example.com")],
    Sign = pg.Sign.ApiKey("PG-API-xxx")
});

var result = await encrypted.UploadAsync();
Console.WriteLine(result.Uuid);
```

See the [full API reference](https://docs.postguard.eu/repos/postguard-dotnet) for all encryption options, recipient types, and upload/notification methods.

## Development

### Prerequisites

- .NET 10.0+ SDK
- Rust toolchain ([rustup](https://rustup.rs/))

### Build native library

The SDK calls the `pg-ffi` crate, one directory up in this repository. Build it first:

```bash
../pg-ffi/build.sh
```

This compiles the crate for your platform and copies the library to `src/runtimes/`, which is not committed. One test (`NativeLibraryTests`) calls into it and fails when it is missing.

### Build the .NET solution

```bash
dotnet build E4A.PostGuard.slnx
dotnet test E4A.PostGuard.slnx
```

### Run the example

See [`examples/pg-dotnet`](https://github.com/encryption4all/postguard-js/tree/main/examples/pg-dotnet)
in the postguard-js workspace. It lived in `encryption4all/postguard-examples`
until that repository was folded in and archived.

It multi-targets `net8.0;net10.0`, so a bare `dotnet run` fails with "Your project
targets multiple frameworks" — pass `-f net10.0`.

## Releasing

`E4A.PostGuard` has no release cycle of its own. It is published to NuGet on every `pg-core` release, at `pg-core`'s version, with the `pg-ffi` libraries for all five platforms built from the same commit in the same workflow run (`delivery.yml`). The csproj reads the version from `pg-core/Cargo.toml`; there is nothing to bump here. A C#-only fix ships with the next `pg-core` release.

Up to 0.6.0 this SDK lived in `encryption4all/postguard-dotnet` and had its own version line; [CHANGELOG.md](./CHANGELOG.md) keeps that record.

## License

MIT
