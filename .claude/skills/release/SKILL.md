---
name: release
description: rdpmanager の新バージョンをリリースする(csproj/wxs のバージョン同期 → self-contained publish → WiX で MSI ビルド → gh release)。「vX.Y.Z をリリースして」と言われたら使う。
---

# リリース手順

引数: 新バージョン `X.Y.Z`。未指定なら現行バージョン(`src/RdpManager/RdpManager.csproj` の `<Version>`)を確認し、ユーザーに新バージョンを確認する。

1. バージョン更新 — **必ず 2 箇所を同期する**:
   - `src/RdpManager/RdpManager.csproj` の `<Version>`
   - `installer/RdpManager.wxs` の `Version=`
2. 実行中の rdpmanager プロセスを kill する
3. self-contained publish:
   `dotnet publish src/RdpManager/RdpManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish-sc`
4. MSI ビルド(**WiX 5 必須** — WiX 7 は有償 OSMF EULA のため使わない。未導入なら `dotnet tool install --global wix --version 5.0.2`):
   `wix build installer/RdpManager.wxs -bindpath publish-sc -o dist/rdpmanager-X.Y.Z.msi`
5. `dist/rdpmanager-X.Y.Z.msi` が生成されたことを確認する
6. バージョン更新のコミットと `gh release create vX.Y.Z dist/rdpmanager-X.Y.Z.msi` は外部公開操作なので、**実行前に必ずユーザーの最終確認を取る**。コミットメッセージに `Closes #N` を書くと issue が自動クローズされる。
