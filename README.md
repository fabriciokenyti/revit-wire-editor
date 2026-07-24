# RevitWireEditor

Plugin Revit **avulso** (Engenharia Moderna) que padroniza a **configuração de condutores e
cabos elétricos** num clique — substitui o fluxo manual de limpar + excluir item a item +
Transferir Normas de Projeto.

**Suporta apenas Revit 2026 e 2027.**

| Revit | .NET | API |
|-------|------|-----|
| 2026 | net8.0-windows | `Revit_All_Main_Versions_API_x64` 2026.0.0 |
| 2027 | net10.0-windows | `Revit_All_Main_Versions_API_x64` 2027.0.0 |

> ℹ️ O Revit 2027 migrou para **.NET 10** (o 2025/2026 usa .NET 8).

## O que faz

Botão único **Configurar Fiação** (aba Complementos → painel *Editor de Fiação*):
deixa a config de condutores/cabos **exatamente** igual ao padrão embutido — cria o que falta e
remove o que estiver fora do padrão. Ação única, reversível com **Ctrl+Z**, com prévia e
confirmação num diálogo estilizado (Em Design System).

Reproduz fielmente (verificado byte a byte): materiais de condutor, materiais isolantes,
classificações de temperatura, tamanhos de condutor, tipos de cabo e tamanhos de cabo —
incluindo a coluna **Utilizável** (relação tipo de cabo ↔ tamanho de cabo).

O padrão vive **congelado** em `RevitWireEditor/Resources/config-seed.json` (embutido no assembly).

## Build

```bash
dotnet build RevitWireEditor/RevitWireEditor.csproj -c R2026   # ou -c R2027
```

O PostBuild faz deploy de desenvolvimento para `%APPDATA%\Autodesk\REVIT\Addins\<versão>\`
(se a pasta existir) **e** popula o bundle em `RevitWireEditor.bundle\Contents\<versão>\`.
Add-in `Application` só carrega ao (re)abrir o Revit.

## Instalador (distribuição)

Usa **Inno Setup 6** e instala o bundle por usuário (sem admin) em
`%APPDATA%\Autodesk\ApplicationPlugins\RevitWireEditor.bundle`.

```bash
# 1. Buildar os dois targets (popula o bundle):
dotnet build RevitWireEditor/RevitWireEditor.csproj -c R2026
dotnet build RevitWireEditor/RevitWireEditor.csproj -c R2027
# 2. Compilar o instalador:
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\RevitWireEditor.iss
# Saída: installer\Output\RevitWireEditor-1.0.0-Setup.exe
```

## Regerar o padrão (se o template mudar)

O `config-seed.json` é congelado. Para atualizá-lo a partir de um novo template-padrão:

1. No Revit, com o template-padrão aberto, rode o comando **Diagnóstico** (classe
   `DiagnosticoCommand` — não está na ribbon; reative temporariamente em `App.cs` se necessário).
   Ele gera `%TEMP%\RevitWireEditor-config-dump.txt` com um dump por reflexão de toda a config.
2. Rode o parser (fora do repo) que converte o dump em `config-seed.json`
   (script de referência: `parse_dump.py`), regravando `RevitWireEditor/Resources/config-seed.json`.
3. Rebuild. Opcional: `compare.py` valida o resultado aplicado contra o seed (0 diferenças).
