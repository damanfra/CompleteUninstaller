# Complete Uninstaller

Desinstalador profundo para Windows 10 e 11. Lista **todos** os programas instalados, inclusive os que o
Painel de Controle não mostra, executa o desinstalador oficial e depois procura e remove as sobras
(pastas, arquivos, atalhos, serviços e tarefas agendadas), sempre com **quarentena e restauração**.

> Fase 1: níveis **Seguro** e **Moderado**. Limpeza de Registro e nível **Avançado** ficam para a fase 2.

## Requisitos

- Windows 10 (2004+) ou Windows 11, x64
- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2022 17.14+ / 2026, Rider ou VS Code com C# Dev Kit (opcional)

## Como compilar e executar

```powershell
cd C:\Users\<voce>\Projetos\CompleteUninstaller
dotnet build
dotnet test                                    # testes da lógica de domínio
dotnet run --project src\CompleteUninstaller.App
```

O app pede elevação (UAC) ao abrir: é necessário para ler HKLM e todos os perfis, mexer em serviços,
tarefas agendadas e Arquivos de Programas. No Visual Studio, abra a solution como administrador para depurar.

Para gerar um executável único:

```powershell
dotnet publish src\CompleteUninstaller.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

## Versão para Linux

Na branch `linux`: o mesmo desinstalador para **Linux x64** (apt/dpkg, dnf/rpm, Flatpak e Snap), com interface
gráfica em [Avalonia](https://avaloniaui.net). Lista os pacotes (escondendo bibliotecas e componentes do
sistema por padrão), mostra o que mais seria removido junto, remove o pacote e procura sobras
(`~/.config`, `~/.local/share`, `~/.cache`, `~/.var/app`, `~/snap`, `/opt`, `/etc`, atalhos `.desktop`,
serviços do systemd), sempre com **quarentena e restauração**.

```bash
dotnet build CompleteUninstaller.Linux.slnf
dotnet run --project src/CompleteUninstaller.App.Avalonia      # abra como usuário comum
dotnet publish src/CompleteUninstaller.App.Avalonia -c Release -r linux-x64 --self-contained false -p:PublishSingleFile=true
```

- Rode como **usuário comum**: o aplicativo pede a senha (polkit/`pkexec`) só para remover o pacote e para mover
  itens fora da pasta pessoal. Requer um agente do polkit na sessão (o padrão nas áreas de trabalho comuns).
- Nunca remove o que pertence a um pacote ainda instalado, nem pacotes essenciais; `apt` e `dnf` são simulados antes.
- Nada de `purge`, `--delete-data` ou `snap --purge`: configuração e dados viram sobras que você revisa.
- **Status:** os parsers e as regras de segurança têm testes, mas os adaptadores ainda **não foram executados em
  um Linux real**. Teste primeiro numa VM ou no WSL2 com WSLg.

## Atualizações automáticas

O Complete Uninstaller (Windows e Linux) verifica as [Releases](https://github.com/damanfra/CompleteUninstaller/releases)
ao abrir e pelo botão **Atualizações**. Se houver versão nova, mostra as novidades e **pergunta** antes de baixar,
instalar e reiniciar. Só instala se o SHA-256 do arquivo conferir com o `SHA256SUMS.txt` da release.

Para publicar uma versão: `git tag v0.2.0` e `git push origin v0.2.0`. A Action compila, carimba a versão
no executável, gera os pacotes e o `SHA256SUMS.txt` e cria a release. A atualização automática exige que o
executável esteja numa pasta com permissão de escrita (ela avisa quando não estiver).

## Arquitetura

```
src/
  CompleteUninstaller.Core            (net10.0 — domínio puro, testável em qualquer SO)
    Models/        InstalledApp, RegistryLocation, flags
    Text/          NameNormalizer (normalização de nomes), ByteSize
    Matching/      AppNameMatcher — heurística de nomes (Exact / Partial / Publisher)
    Safety/        PathGuard — última barreira antes de qualquer item entrar na lista
    Leftovers/     LeftoverItem, LeftoverCollector (deduplicação, filtro por nível)
    Commands/      CommandLineParser, UninstallCommandBuilder
    Inventory/     InventoryMerger (une Registro + MSI + Store sem duplicatas)

  CompleteUninstaller.Infrastructure  (net10.0-windows — adaptadores Windows)
    Inventory/     RegistryUninstallSource, MsiSource (msi.dll), AppxSource (PackageManager)
    Uninstall/     UninstallRunner + TrackedProcess (Job Object)
    Leftovers/     ScanContext, InstallDirResolver, FolderScanner, ShortcutScanner,
                   ServiceScanner, ScheduledTaskScanner, SafetyRules
    Removal/       RemovalService, QuarantineStore, FileOps, ServiceOps, TaskOps,
                   RestorePointService
    Native/        P/Invoke (msi, kernel32, advapi32) e IShellLink

  CompleteUninstaller.App             (WPF, MVVM sem dependências externas, tema Fluent nativo)

tests/
  CompleteUninstaller.Core.Tests      (xUnit)
```

Nenhum pacote NuGet de terceiros no app: tudo é .NET + APIs do Windows.

## De onde vem a lista de programas

| Fonte | O que encontra |
|---|---|
| `HKLM\...\Uninstall` (64 e 32 bits) | O que o Painel de Controle lê, **incluindo** `SystemComponent=1` e atualizações |
| `HKEY_USERS\<SID>\...\Uninstall` | Instalações "só para mim" de todos os usuários com sessão carregada |
| Windows Installer (`MsiEnumProductsEx`) | MSIs sem chave Uninstall visível, inclusive de outros usuários |
| `PackageManager` (WinRT) | Apps da Store / MSIX do usuário atual |

O Windows Installer **não** é consultado pelo WMI `Win32_Product` (que dispara verificação/reparo de todos os MSIs).

## Como a desinstalação funciona

1. **Ponto de restauração** (opcional) via `Checkpoint-Computer`. O Windows só cria um a cada 24 h por padrão.
2. **Desinstalador oficial** dentro de um **Job Object**: o app espera todos os processos filhos terminarem
   (Inno Setup, NSIS etc. se relançam a partir da pasta Temp). MSI usa `msiexec /x` (e não `/I`).
   Há um botão "O desinstalador já terminou" para quando ele abre o navegador no final.
3. **Confere** se o programa saiu mesmo do Registro/MSI/Store. Se não saiu, pergunta antes de continuar.
4. **Varredura de sobras** com o inventário **atualizado**: o que continua instalado é protegido.
5. **Revisão**: confiança alta vem marcada; confiança média vem desmarcada.
6. **Remoção para a quarentena** (`%ProgramData%\CompleteUninstaller\Quarantine`):
   - pastas/arquivos/atalhos são movidos (no mesmo volume é um rename atômico); itens em uso são copiados
     e o original é apagado na reinicialização (`MoveFileEx` + `MOVEFILE_DELAY_UNTIL_REBOOT`);
   - serviços são parados e excluídos, com a configuração guardada para recriação;
   - tarefas agendadas têm o XML guardado antes de `schtasks /Delete`.

Tudo é registrado em `%ProgramData%\CompleteUninstaller\Logs` (inclusive o que a proteção bloqueou).

## Níveis de limpeza

**Seguro** — só ligação comprovada com o programa:
- pasta de instalação registrada (`InstallLocation`) e pastas do ícone/desinstalador com o nome do programa;
- pastas com o **nome exato** do programa em Arquivos de Programas, ProgramData e AppData (Roaming, Local,
  LocalLow, Programs) de **todos os perfis**, e subpastas do fabricante (`AppData\Roaming\Mozilla\Firefox`);
- cache do instalador (`ProgramData\Package Cache\{código}`) e dados de apps da Store (`Local\Packages\<família>`);
- atalhos (Menu Iniciar/Área de Trabalho) que apontam para a pasta do programa, ou com o nome exato e destino inexistente;
- serviços Win32 e tarefas agendadas que executam arquivos da pasta do programa.

**Moderado** — tudo acima, mais itens por **semelhança de nome** (vêm desmarcados):
- pastas com nome parecido (`Code` para *Visual Studio Code*, `Firefox Profiles` para *Firefox*);
- serviços e tarefas com o nome do programa;
- pasta do fabricante quando só contém dados deste programa e nenhum outro produto do fabricante está instalado.

## Proteções (PathGuard + regras)

- Nunca remove: raízes de unidade, `Windows`, `Program Files`/`Common Files`/`ProgramData` (as pastas em si),
  `ProgramData\Microsoft`, `AppData\*\Microsoft`, Temp, Documentos/Imagens/Downloads/OneDrive, perfis e `Default`,
  pastas de sistema do Menu Iniciar, `WindowsApps`, `dotnet`, a pasta do próprio app e a quarentena.
- Nunca remove um caminho que **contenha** ou **esteja dentro** da pasta de outro programa instalado
  (incluindo pastas deduzidas do ícone e do desinstalador dele).
- Nomes genéricos (`Update`, `Cache`, `Data`, `Microsoft`, `Common Files`...) nunca identificam um programa.
- Pasta com nome de outro programa instalado não entra como sobra.
- Não atravessa junções/links simbólicos; serviços com executável dentro de `C:\Windows` são ignorados.

## Roteiro — fase 2

- [ ] Limpeza de Registro: `Software\<Fabricante>\<Produto>` (HKLM/HKCU de todos os perfis, carregando `NTUSER.DAT`),
      chave Uninstall órfã, entradas Run/RunOnce, registros COM (CLSID/TypeLib/AppID), associações de arquivo,
      extensões de shell, `SharedDLLs` — com backup `.reg` antes de remover.
- [ ] Nível **Avançado** (busca por conteúdo: caminhos citados em valores do Registro, GUIDs, nomes de executáveis).
- [ ] Drivers de kernel, regras de firewall, entradas no PATH.
- [ ] "Remoção forçada" para programas com desinstalador quebrado.
- [ ] Rastreamento de instalação (snapshot antes/depois ou ETW).
- [ ] Parâmetros silenciosos conhecidos (Inno `/VERYSILENT`, NSIS `/S`).
- [ ] Pacotes da Store de todos os usuários e remoção de pacotes provisionados.
