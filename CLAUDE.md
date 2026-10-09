# CLAUDE.md — Complete Uninstaller

Instruções para o Claude ler no início de cada conversa sobre este projeto.
Mantenha este arquivo atualizado quando decisões importantes mudarem.

## O que é

Desinstalador profundo para **Windows 10 (2004+) e 11, x64**. O app:
1. lista todos os programas instalados, inclusive os que o Painel de Controle não mostra;
2. executa o desinstalador oficial;
3. procura sobras (pastas, arquivos, atalhos, serviços, tarefas agendadas);
4. move o que o usuário aprovar para uma **quarentena restaurável**.

Projeto pessoal do Daniel. Pasta: `C:\Users\daniel.msouza\Projetos\CompleteUninstaller`.

## Estado atual (atualize sempre)

- **Fase 1 implementada:** níveis **Seguro** e **Moderado**.
- **Fase 2 (não implementada):** limpeza de **Registro** e nível **Avançado**. Veja "Roteiro" no `README.md`.
- ✅ **Build OK e 67 testes passando** (07/10/2026, Visual Studio 2026). Só foi preciso corrigir a falta de
  `using System.IO;` no projeto WPF.
- 07/10: adicionados ícones na lista e filtros "Ocultar itens do Windows" (ligado por padrão) e
  "Ocultar apps da Microsoft". Aguardando build/teste do Daniel.
- ⏳ **Ainda não testado em uso real**: abrir o app, listar, desinstalar, revisar sobras, quarentena e restauração.
  Ao retomar, pergunte como foram esses testes e peça o log (`%ProgramData%\CompleteUninstaller\Logs`) se algo falhou.

## Stack e decisões

- **C# / .NET 10**, solution clássica `CompleteUninstaller.sln`.
- **WPF** com o tema **Fluent nativo** (`ThemeMode.System` em `App.xaml.cs`, sob `#pragma warning disable WPF0001`).
  Não usamos WPF-UI nem outras bibliotecas de UI.
- **MVVM próprio e mínimo** (`App/Mvvm`: `ObservableObject`, `RelayCommand`, `AsyncRelayCommand`).
  Não usamos CommunityToolkit.
- **Nenhum pacote NuGet de terceiros** nos projetos do app; só o projeto de testes usa xUnit.
- **P/Invoke escrito à mão** em `Infrastructure/Native` (`DllImport`), sem CsWin32.
- Log próprio em arquivo (`Infrastructure/Logging/Log.cs`), sem Serilog.
- O app roda como **administrador** (`app.manifest` com `requireAdministrator`).
- Arquitetura em camadas, no estilo portas e adaptadores:
  - `CompleteUninstaller.Core` (`net10.0`): domínio puro, **sem API do Windows**, testável em qualquer SO.
  - `CompleteUninstaller.Infrastructure` (`net10.0-windows10.0.19041.0`): adaptadores Windows
    (Registro, msi.dll, PackageManager/WinRT, SCM, schtasks, IShellLink).
  - `CompleteUninstaller.App` (WPF): `AppServices` faz a composição manual, sem contêiner de DI.
  - `tests/CompleteUninstaller.Core.Tests` (xUnit): testa só o Core.
- **Namespace:** use `CompleteUninstaller.Infrastructure`, nunca `CompleteUninstaller.Windows`,
  porque esse nome colidiria com o namespace `Windows.*` do WinRT.

## Mapa do código

| Área | Arquivos-chave |
|---|---|
| Modelo do programa | `Core/Models/InstalledApp.cs` (`AppSource`, `AppFlags`, `RegistryLocation`); `AppClassifier.cs` (filtros "itens do Windows" e "apps da Microsoft") |
| Ícones | `Infrastructure/Inventory/IconLocator.cs` (DisplayIcon → logotipo da Store → exe da pasta) + `App/Services/IconLoader.cs` (ExtractIconEx, cache, carga em segundo plano) |
| Heurística de nomes | `Core/Text/NameNormalizer.cs`, `Core/Matching/AppNameMatcher.cs` (`Exact` / `Partial` / `Publisher`) |
| Proteção de caminhos | `Core/Safety/PathGuard.cs` + `Infrastructure/Leftovers/SafetyRules.cs` |
| Sobras (domínio) | `Core/Leftovers/LeftoverItem.cs` (`Confidence`, `CleanupLevel`), `LeftoverCollector.cs` |
| Comando de desinstalação | `Core/Commands/CommandLineParser.cs`, `UninstallCommandBuilder.cs` |
| Inventário | `Infrastructure/Inventory/*`: `RegistryUninstallSource`, `MsiSource`, `AppxSource`, `InventoryService`; mesclagem em `Core/Inventory/InventoryMerger.cs` |
| Execução do desinstalador | `Infrastructure/Uninstall/TrackedProcess.cs` (Job Object), `UninstallRunner.cs` |
| Varredura | `Infrastructure/Leftovers/LeftoverScanner.cs` (orquestra), `ScanContext`, `InstallDirResolver`, `FolderScanner`, `ShortcutScanner`, `ServiceScanner`, `ScheduledTaskScanner` |
| Remoção e quarentena | `Infrastructure/Removal/*`: `RemovalService`, `QuarantineStore`, `FileOps`, `ServiceOps`, `TaskOps`, `RestorePointService` |
| UI | `App/Views/*Window.xaml`, `App/ViewModels/*` (assistente com passos `Options → Working → Review → Done`) |

## Regras de segurança (não negociáveis)

Este app apaga coisas. Qualquer mudança deve preservar estas regras:

1. **Nada é apagado direto.** Arquivos e pastas vão para a quarentena
   (`%ProgramData%\CompleteUninstaller\Quarantine\<sessão>\manifest.json` + `items\NNNN\...`).
   Serviços e tarefas guardam backup (configuração do serviço e XML da tarefa) antes da exclusão.
2. **Todo caminho passa pelo `PathGuard`** (`ScanContext.TryAddPath`) antes de virar sobra.
   - Nunca remover: raízes de unidade, `Windows`, as pastas raiz de Program Files, Common Files e ProgramData,
     `ProgramData\Microsoft`, `AppData\*\Microsoft`, Temp, pastas pessoais (Documentos, Downloads, OneDrive...),
     pastas de sistema do Menu Iniciar, `WindowsApps`, `dotnet`, a pasta do próprio app e a quarentena.
   - A regra mais específica vence: o Menu Iniciar é uma exceção permitida dentro de `ProgramData\Microsoft`.
3. **Nunca remover um caminho que contenha a pasta de outro programa instalado, ou que esteja dentro dela.**
   Isso vale também para pastas deduzidas do ícone e do desinstalador desse outro programa.
   A varredura usa o inventário **lido de novo depois** da desinstalação.
4. **Nomes genéricos** (`Update`, `Cache`, `Data`, `Microsoft`, `Common Files`...) nunca identificam um programa.
   Uma pasta cujo nome corresponde de forma exata a outro programa instalado (`BelongsToAnotherApp`) é ignorada.
5. **Confiança:** **Alta** = ligação comprovada (pasta registrada, atalho ou serviço apontando para a pasta,
   nome exato) e vem **marcada**. **Média** = semelhança de nome, aparece **só no Moderado** e vem **desmarcada**.
6. Não atravessar junções nem links simbólicos. Ignorar serviços cujo executável está em `C:\Windows`.
   Drivers de kernel ficam fora até a fase 2.
7. Usar `msiexec /x` (nunca `/I`) e **nunca** o WMI `Win32_Product`.
8. Arquivo em uso: copiar para a quarentena e agendar a exclusão do original com
   `MoveFileEx(..., MOVEFILE_DELAY_UNTIL_REBOOT)`.
9. Registrar no log tudo o que for encontrado, ignorado pela proteção e removido.

Ao mexer na heurística, **adicione ou ajuste testes** em `tests/` cobrindo falsos positivos.

## Convenções

- Interface, mensagens, comentários e documentação em **português do Brasil**. Identificadores em inglês.
- File-scoped namespaces, `Nullable` e `ImplicitUsings` ligados (`Directory.Build.props`).
- Lógica pura e testável vai para o **Core**. Qualquer coisa que toque o Windows vai para a **Infrastructure**.
- Enumeração de arquivos sempre tolerante a erro (`FileSystemHelper`).
- No projeto WPF (`App`) o `System.IO` **não** entra nos implicit usings (conflito com `System.Windows.Shapes.Path`):
  declare `using System.IO;` explicitamente ao usar `File`, `Directory` ou `Path`.
- Não usar `ICollectionView.DeferRefresh()` ao alterar a coleção de origem de uma DataGrid: lança
  `InvalidOperationException` (CurrentItem). Limpe a seleção e preencha a coleção normalmente.
- Commands assíncronos usam `AsyncRelayCommand`. Exceções não tratadas viram MessageBox e entrada no log.

## Comandos

```powershell
dotnet build
dotnet test
dotnet run --project src\CompleteUninstaller.App      # pede UAC
dotnet publish src\CompleteUninstaller.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

## Ambiente de trabalho do Claude

- Os arquivos ficam no computador do Daniel (Windows). O Claude edita uma cópia no ambiente de nuvem
  e grava de volta na pasta do projeto.
- O ambiente de nuvem **não compila .NET**: não há SDK e o NuGet está bloqueado.
  - Para verificar a sintaxe do C#, dá para compilar o tree-sitter + tree-sitter-c-sharp clonados do GitHub.
  - A compilação de verdade depende do Daniel rodar `dotnet build` e colar os erros.
- Não reescrever arquivos inteiros sem necessidade; preferir edições pontuais.
- O Daniel usa o **Visual Studio 2026**. Se um arquivo editado pelo Claude estiver aberto no VS, o VS pode
  salvar por cima a versão antiga. Depois de gravar uma correção, confira o arquivo na máquina dele
  e lembre-o de recarregar os arquivos ("Recarregar tudo") antes de compilar.

## Roteiro — fase 2

- Limpeza de Registro com backup `.reg`. Alvos:
  - `Software\<Fabricante>\<Produto>`, em HKLM e no HKCU de todos os perfis (carregando `NTUSER.DAT`);
  - chave Uninstall órfã;
  - entradas Run e RunOnce;
  - registros COM (CLSID, TypeLib, AppID);
  - associações de arquivo, extensões de shell e `SharedDLLs`.
- Nível Avançado: busca por conteúdo (caminhos e GUIDs citados em valores do Registro).
- Drivers, regras de firewall e entradas no PATH.
- Remoção forçada e rastreamento de instalação (snapshot antes/depois ou ETW).
- Switches silenciosos conhecidos (Inno `/VERYSILENT`, NSIS `/S`).
- Store: pacotes de todos os usuários e pacotes provisionados.
