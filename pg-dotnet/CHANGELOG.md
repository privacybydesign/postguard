# Changelog

## How this package is versioned

From the first release after 0.6.0, `E4A.PostGuard` has no release cycle of its
own. It is published to NuGet on **every** `pg-core` release, at `pg-core`'s
version, wrapping the `pg-ffi` built from the same commit. There is no
`E4A.PostGuard` tag series any more, and there are no new per-version entries
in this file.

**For what changed in a version after 0.6.0, read
[`pg-core`'s changelog](../pg-core/CHANGELOG.md) for the same version number.**
A change to the C# code itself is listed there too, under the `pg-core` release
it shipped with.

The entries below are the record of the SDK's own version line, from when it
lived in `encryption4all/postguard-dotnet`. That repository is archived and
still holds the commits and issues these entries link to.

## [0.6.0](https://github.com/encryption4all/postguard-dotnet/compare/E4A.PostGuard-v0.5.0...E4A.PostGuard-v0.6.0) (2026-07-27)


### ⚠ BREAKING CHANGES

* NetworkException.Body was removed in #43 (GHSA info-exposure fix) but landed as a plain fix:, so release-please computed 0.5.1. The public-API tracking added in #52 records the removal as *REMOVED*; this marker corrects the next release to reflect it.

### Bug Fixes

* emit inclusive range-end in Cryptify Content-Range header ([#34](https://github.com/encryption4all/postguard-dotnet/issues/34)) ([73c2e43](https://github.com/encryption4all/postguard-dotnet/commit/73c2e43993b805b188e07fe0d2073e36e335098b)), closes [#28](https://github.com/encryption4all/postguard-dotnet/issues/28)
* harden ZIP entry-name handling in ZipHelper ([#42](https://github.com/encryption4all/postguard-dotnet/issues/42)) ([2ffe84e](https://github.com/encryption4all/postguard-dotnet/commit/2ffe84e9ec811c414104eeae9937cbdfe3cd576c))
* NetworkException no longer exposes raw upstream response bodies ([7d2e1fb](https://github.com/encryption4all/postguard-dotnet/commit/7d2e1fb3ad408cb2aab1dd92fb2bc9f2d220945f))
* reject duplicate recipient policy keys in BuildPolicyJson ([#48](https://github.com/encryption4all/postguard-dotnet/issues/48)) ([f5ad9b8](https://github.com/encryption4all/postguard-dotnet/commit/f5ad9b878391c9e59945404b3b2b8ec8cfe4bb59))
* stop leaking raw upstream body in NetworkException ([#41](https://github.com/encryption4all/postguard-dotnet/issues/41)) ([#43](https://github.com/encryption4all/postguard-dotnet/issues/43)) ([135dee3](https://github.com/encryption4all/postguard-dotnet/commit/135dee32e0954c3735ec2f69da58a7b18395fa52))

## [0.5.0](https://github.com/encryption4all/postguard-dotnet/compare/E4A.PostGuard-v0.4.1...E4A.PostGuard-v0.5.0) (2026-06-19)


### Features

* send X-POSTGUARD-CLIENT-VERSION on every request ([#33](https://github.com/encryption4all/postguard-dotnet/issues/33)) ([23eeb00](https://github.com/encryption4all/postguard-dotnet/commit/23eeb00728eefa642b057647e3273305651e8c13))


### Bug Fixes

* include request URL in NetworkException ([#31](https://github.com/encryption4all/postguard-dotnet/issues/31)) ([c33860b](https://github.com/encryption4all/postguard-dotnet/commit/c33860bdcfd979db26323ce16405a005d13f2537)), closes [#26](https://github.com/encryption4all/postguard-dotnet/issues/26)


### Performance Improvements

* avoid per-chunk byte[] copy in CryptifyClient.StoreChunkAsync ([#30](https://github.com/encryption4all/postguard-dotnet/issues/30)) ([a3762d0](https://github.com/encryption4all/postguard-dotnet/commit/a3762d071555996b52fc6cc484e0e95f96d48a72)), closes [#27](https://github.com/encryption4all/postguard-dotnet/issues/27)

## [0.4.1](https://github.com/encryption4all/postguard-dotnet/compare/E4A.PostGuard-v0.4.0...E4A.PostGuard-v0.4.1) (2026-05-17)


### Bug Fixes

* reject unknown recipient BaseType in BuildPolicyJson ([#20](https://github.com/encryption4all/postguard-dotnet/issues/20)) ([8128f92](https://github.com/encryption4all/postguard-dotnet/commit/8128f9254e2dfe7fa4248210d06ac49a4245b8b7))
* reuse single HttpClient across SealPipeline calls ([#19](https://github.com/encryption4all/postguard-dotnet/issues/19)) ([fb3e3d5](https://github.com/encryption4all/postguard-dotnet/commit/fb3e3d570ae0df0e731da2f776bc77765bd4c249)), closes [#11](https://github.com/encryption4all/postguard-dotnet/issues/11)

## [0.4.0](https://github.com/encryption4all/postguard-dotnet/compare/E4A.PostGuard-v0.3.0...E4A.PostGuard-v0.4.0) (2026-05-16)


### Features

* multi-target net8.0 and net10.0 ([f25fa46](https://github.com/encryption4all/postguard-dotnet/commit/f25fa46029142ebd303011e648b5ee952b47aa93))


### Bug Fixes

* reject http:// URLs in PostGuardConfig to prevent plaintext credential leaks ([3fac2a8](https://github.com/encryption4all/postguard-dotnet/commit/3fac2a883252cbbc0cef75f39d891265df221bd1))
* validate PkgUrl and CryptifyUrl are https:// in PostGuardConfig ([fe6d4eb](https://github.com/encryption4all/postguard-dotnet/commit/fe6d4eb20a795696525fad367a4f241483002d00)), closes [#14](https://github.com/encryption4all/postguard-dotnet/issues/14)

## [0.3.0](https://github.com/encryption4all/postguard-dotnet/compare/E4A.PostGuard-v0.2.1...E4A.PostGuard-v0.3.0) (2026-05-02)


### Features

* **upload:** silent-by-default, opt-in Notify.Recipients / Notify.Sender ([b407942](https://github.com/encryption4all/postguard-dotnet/commit/b407942df70e47f5c7b6d470f0cfd92d7ab792bf))
* **upload:** silent-by-default, opt-in Notify.Recipients/Notify.Sender ([58a3d03](https://github.com/encryption4all/postguard-dotnet/commit/58a3d0392ef28d3c103edca888a517e266479122))

## [0.2.1](https://github.com/encryption4all/postguard-dotnet/compare/E4A.PostGuard-v0.2.0...E4A.PostGuard-v0.2.1) (2026-04-10)


### Bug Fixes

* update pg-ffi native binaries to streaming sealer ([90da661](https://github.com/encryption4all/postguard-dotnet/commit/90da66100162dadf1ab249fea77bc548ae28bbbf))

## [0.2.0](https://github.com/encryption4all/postguard-dotnet/compare/E4A.PostGuard-v0.1.2...E4A.PostGuard-v0.2.0) (2026-04-10)


### Features

* implement PostGuard .NET SDK ([197dcfb](https://github.com/encryption4all/postguard-dotnet/commit/197dcfb2dd97b4977fcf81ad0a9b6bea689ab14d))


### Bug Fixes

* remove invalid release-please action inputs ([588221d](https://github.com/encryption4all/postguard-dotnet/commit/588221d90894ef190533d9eee87c1742cc00bad8))
* set Content-Range as content header, not request header ([417b7e6](https://github.com/encryption4all/postguard-dotnet/commit/417b7e681c29a414f08fcacaa364e9306e40eaeb))
* use gh release download to fetch pg-ffi native libraries ([1c2a544](https://github.com/encryption4all/postguard-dotnet/commit/1c2a544581588943648467893264d5b7be086c40))
* use Node.js 24 for actions and target .NET 10 in CI ([a06e56a](https://github.com/encryption4all/postguard-dotnet/commit/a06e56a32b0e2254bcd2e170b49aa399c6c200b3))
* use None items for native libs to ensure proper NuGet runtime bundling ([3203792](https://github.com/encryption4all/postguard-dotnet/commit/32037921a1900d645f23bc517d153b6a48f85d52))
* use NuGet trusted publishing instead of API key ([b8bbb9c](https://github.com/encryption4all/postguard-dotnet/commit/b8bbb9cf2c26fdd29c082cdee6adcfe32c36d31a))
* use NuGet/login action for trusted publishing OIDC flow ([0d1056f](https://github.com/encryption4all/postguard-dotnet/commit/0d1056f92c4856a92d26a95a6cf5ca26e4d586ec))
* use single-line commands in publish workflow ([f912f72](https://github.com/encryption4all/postguard-dotnet/commit/f912f727cb0260afea9211eb735edc0799d76ae1))

## [0.1.2](https://github.com/encryption4all/postguard-dotnet/compare/v0.1.1...v0.1.2) (2026-04-10)


### Bug Fixes

* set Content-Range as content header, not request header ([417b7e6](https://github.com/encryption4all/postguard-dotnet/commit/417b7e681c29a414f08fcacaa364e9306e40eaeb))
