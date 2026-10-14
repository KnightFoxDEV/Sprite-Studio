# Sprite Studio

Editor desktop para Windows, feito em C# e WPF. Este guia descreve como compilar o aplicativo a partir do código-fonte.

## Requisitos

- Windows 10 ou 11 de 64 bits.
- SDK do **.NET 10** para Windows. Ele inclui os componentes para compilar aplicativos WPF.
- Acesso à internet na primeira compilação para baixar `SharpCompress` (versão 0.50.4) do NuGet.
- Visual Studio é opcional. Para usá-lo, instale o workload **Desenvolvimento para desktop com .NET**.

## Compilar e executar

Abra o PowerShell nesta pasta (`Sprite Studio`) e execute:

```powershell
dotnet restore .\SpriteStudio.csproj
dotnet build .\SpriteStudio.csproj -c Release
```

Para compilar e abrir o aplicativo de uma vez:

```powershell
dotnet run --project .\SpriteStudio.csproj -c Release
```

O executável da compilação normal fica em `bin\Release\net10.0-windows\SpriteStudio.exe`. Essa compilação usa o runtime do .NET 10 instalado no computador.

## Gerar uma versão para distribuir

Para criar um executável único para Windows x64, incluindo o runtime do .NET, execute no PowerShell, ainda nesta pasta:

```powershell
dotnet publish .\SpriteStudio.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\publish\win-x64
```

O aplicativo publicado fica em `publish\win-x64\SpriteStudio.exe` e pode rodar em Windows x64 sem instalar o .NET separadamente. Para publicar para outra arquitetura, altere `win-x64` para o identificador correspondente.

## Dependências do projeto

- **WPF** e bibliotecas do .NET, incluídas no SDK do .NET para Windows.
- **SharpCompress 0.50.4**, restaurado automaticamente do NuGet durante a compilação.
- Recursos incluídos nesta pasta: `Assets` e `config/versions.xml`.

Este repositório contém o código-fonte do Sprite Studio.
