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
- ✅ **Build OK e testes passando** (07/10/2026, Visual Studio 2026; hoje são 358 com a branch `linux`). Só foi preciso corrigir a falta de
  `using System.IO;` no projeto WPF.
- 07/10: adicionados ícones na lista e filtros "Ocultar itens do Windows" (ligado por padrão) e
  "Ocultar apps da Microsoft". Aguardando build/teste do Daniel.
- 10/10: primeiro teste real (Elgato Stream Deck) funcionou. Ajustes pedidos: o log rola sozinho até o fim
  (`App/Behaviors/AutoScroll.cs`), botão final "Concluir", e a tela de opções lembra o nível, o ponto de
  restauração e a desinstalação silenciosa (`Infrastructure/Settings/UserSettings.cs`,
  `%LocalAppData%\CompleteUninstaller\settings.json`). "Não executar o desinstalador" não é lembrado de propósito.
  Aguardando build do Daniel.
- 10/10: pacote da Store (Forza Horizon 6) travou em "Executando o desinstalador": `RemovePackageAsync` não
  respeitava o botão "O desinstalador já terminou". Agora respeita, mostra o progresso, pula pacote que já não está
  registrado e registra no log o estado do pacote (`PackageStatus`).
- ⏳ **Ainda não testado em uso real**: abrir o app, listar, desinstalar, revisar sobras, quarentena e restauração.
  Ao retomar, pergunte como foram esses testes e peça o log (`%ProgramData%\CompleteUninstaller\Logs`) se algo falhou.

## Versão Linux (branch `linux`)

Segunda frente do projeto: o mesmo desinstalador para Linux (apt/dpkg, dnf/rpm, Flatpak e Snap), com a mesma
filosofia (nada é apagado direto; quarentena restaurável). O app do Windows (WPF) **não foi alterado**.

- **Projetos novos:** `CompleteUninstaller.Infrastructure.Linux` (`net10.0`, compila em qualquer SO, só executa no
  Linux) e `CompleteUninstaller.App.Avalonia` (GUI). `CompleteUninstaller.Linux.slnf` é o filtro de solution
  só com o que o Linux usa (o WPF não compila lá).
- **Exceção à regra "sem NuGet de terceiros":** a GUI do Linux usa **Avalonia 12.1.x** (+ `Avalonia.Controls.DataGrid`).
  Escolha do Daniel. O Core e a Infrastructure continuam sem pacotes.
- **Core compartilhado:** `PathGuard` e `LeftoverCollector` aceitam `IPathRules` (padrão: Windows, comportamento
  idêntico). `UnixPath`/`UnixPathRules` são texto puro, diferenciam maiúsculas e **rejeitam `..`**.
  `Core/Linux/*` tem os parsers (dpkg, rpm, Flatpak, Snap, apt/dnf simulado, `.desktop`) e `LinuxUninstallCommands`.
  Tudo testável no Windows com amostras de saída.
- **Sem WSL/Linux na máquina do Daniel até agora:** os adaptadores que executam comandos (`CommandRunner`,
  inventário, remoção, auxiliar) **nunca foram executados em Linux de verdade**. Só compilam e os parsers/validações
  têm testes. Ao retomar, peça para testar numa VM/WSL e colar o log (`~/.local/share/CompleteUninstaller/Logs`).

### Regras de segurança específicas do Linux (além das abaixo)

1. A GUI **não roda como root**. Operações com privilégio: `pkexec apt-get/dnf/snap ...` para o pacote e
   `pkexec <app> --helper` (`PrivilegedHelper`) para mover itens do sistema. O auxiliar **não confia no pedido**:
   valida de novo caminho, tipo, ids e posse por pacote instalado, e só aceita `move`, `restore` e `purge`.
2. **`UnixSafetyRules`**: lista de áreas permitidas (pasta pessoal e `/opt /etc /srv /usr/share /usr/lib /usr/local
   /var/lib /var/cache /var/log /var/snap`); o resto é vetado. Na pasta pessoal só valem pastas ocultas e `~/snap`
   (nunca Documentos, Projetos...). Árvores como `/usr/bin`, `/var/lib/dpkg`, `/etc/ssh`, `~/.ssh` são intocáveis.
3. **Posse por pacote:** caminho que um pacote **ainda instalado** possui (`dpkg -S` / `rpm -qf`) nunca vira sobra.
   Pacote em estado `rc` (removido com configuração) **não** protege. Caminhos com `* ? [ \` ficam protegidos.
4. `dpkg -L`/`rpm -ql` (lidos **antes** de remover) só indicam candidatos; incluem pastas-pai compartilhadas.
5. Remoção: `apt-get remove` (nunca purge), `flatpak uninstall` sem `--delete-data`, `snap remove` sem `--purge`.
   apt/dnf são **simulados antes** e os dependentes que também seriam removidos exigem confirmação.
   Pacotes essenciais (`Essential`, prioridade required/important) não podem ser removidos.
6. Links simbólicos nunca viram sobra. Mover usa `mv --` (preserva dono e permissões entre dispositivos).
7. Duas quarentenas: `~/.local/share/CompleteUninstaller/Quarantine` (pasta pessoal) e
   `/var/lib/CompleteUninstaller/Quarantine` (sistema, só o auxiliar acessa; o manifesto do usuário a referencia).
8. Nomes de pacote são validados por regex e executados com `ArgumentList` (sem shell), sempre com `LC_ALL=C`.

### Ícone

`assets/make-icon.ps1` (PowerShell + System.Drawing) gera `assets/icon.ico` (16 a 256 px: ícone do `.exe` e das janelas WPF)
e `assets/icon.png` (janelas do Avalonia). Para mudar o desenho, edite o script e rode-o; os dois arquivos são versionados.
A versão (`vX.Y.Z`) aparece no título da janela e na barra de status.

### Auto-atualização (Windows e Linux)

- Projeto `CompleteUninstaller.Updater` (`net10.0`, só BCL) + lógica pura em `Core/Updates` (`AppVersion`, `UpdatePlanner`).
  `UpdateFlow` é o fluxo compartilhado pelas duas UIs: verifica (silencioso ao abrir; botão "Atualizações" responde
  sempre), **pergunta**, baixa, confere, troca o executável e reinicia. Nunca atualiza sem confirmação.
- Fonte: `GET api.github.com/repos/damanfra/CompleteUninstaller/releases/latest` (o repositório precisa ser **público**).
  Ignora rascunhos e pré-lançamentos. Pacotes: `CompleteUninstaller-win-x64.zip` (`CompleteUninstaller.exe`) e
  `CompleteUninstaller-linux-x64.tar.gz` (`complete-uninstaller`).
- **Integridade:** só instala se o SHA-256 do pacote conferir com o `SHA256SUMS.txt` da mesma release (gerado pela Action);
  sem esse arquivo a atualização é recusada. Links só de HTTPS `github.com/{repo}/releases/download/`. Limite de 500 MB.
  Isso protege de arquivo corrompido ou de link trocado, **não** de uma release publicada por quem controla o repositório
  (não há assinatura de código).
- Troca do arquivo: Windows renomeia o `.exe` em uso para `.old` e o apaga na próxima abertura; Linux troca por
  renomeação atômica e restaura `chmod`. Não funciona via `dotnet app.dll` nem em pasta sem permissão de escrita (avisa).
- **Versão** vem da tag: a Action usa `-p:Version=1.2.3` (tag `v1.2.3`); sem tag é `0.0.0-dev`. Build local = `0.1.0`
  (`Directory.Build.props`). Para lançar: `git tag v0.2.0 && git push origin v0.2.0`.
- Nada do fluxo de rede/troca foi testado com uma release real ainda: só com servidor simulado nos testes.

Limitações conhecidas: só o usuário atual é varrido (não os outros `/home/*`); sem cron; `~/.mozilla`-style
(pasta com nome diferente do pacote) não é achada; ícones só PNG; `dnf remove --assumeno` pede senha (o dnf pede duas vezes: simulação e remoção); o auxiliar sob `pkexec` precisa achar o .NET (repassamos `DOTNET_ROOT`; para testar, publique self-contained).

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
| UI | `App/Views/*Window.xaml`, `App/ViewModels/*` (assistente com passos `Options → Working → Review → Done`), `App/Behaviors/AutoScroll.cs` |
| Preferências | `Infrastructure/Settings/UserSettings.cs` (últimas opções da desinstalação, em `%LocalAppData%`) |

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

## Comandos do Linux

```bash
dotnet build CompleteUninstaller.Linux.slnf
dotnet test tests/CompleteUninstaller.Core.Tests
dotnet run --project src/CompleteUninstaller.App.Avalonia
dotnet publish src/CompleteUninstaller.App.Avalonia -c Release -r linux-x64 --self-contained false -p:PublishSingleFile=true
```

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
