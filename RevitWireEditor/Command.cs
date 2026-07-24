#region Namespaces
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
#endregion

namespace RevitWireEditor
{
    /// <summary>
    /// Fatia 1 do Editor de Fiacao: remove os caracteres '[' e ']' dos nomes dos
    /// materiais de fiacao / condutores. Mostra uma previa e pede confirmacao antes
    /// de gravar. Renomear nao quebra referencias (elas usam Id interno).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class LimparColchetesCommand : IExternalCommand
    {
        private const string Titulo = "Editor de Fiacao";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            List<Element> itens;
            try
            {
                itens = WireMaterialGateway.GetItensRenomeaveis(doc);
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show(Titulo, "Nao foi possivel ler os itens de fiacao/cabo:\n\n" + ex.Message);
                return Result.Failed;
            }

            // Seleciona apenas os que tem '[' ou ']' e ja calcula o nome novo.
            var alvos = itens
                .Select(e => new Rename(e, e.Name))
                .Where(r => r.Antigo.IndexOf('[') >= 0 || r.Antigo.IndexOf(']') >= 0)
                .ToList();

            if (alvos.Count == 0)
            {
                TaskDialog.Show(Titulo,
                    $"Nenhum item com '[' ou ']' encontrado.\n\nTotal de itens verificados (materiais + tipos de cabo): {itens.Count}.");
                return Result.Succeeded;
            }

            // Pre-2026: o nome dos materiais de fiacao e read-only na API (Revit lanca
            // InvalidOperationException). Avisa em vez de tentar e falhar em lote.
            if (!WireMaterialGateway.RenomearSuportado)
            {
                TaskDialog.Show(Titulo,
                    $"Encontrei {alvos.Count} nome(s) com '[' ou ']', mas esta versao do Revit " +
                    "NAO permite renomear materiais de fiacao pela API (o nome e read-only; " +
                    "limitacao da Autodesk, corrigida no Revit 2026).\n\n" +
                    "Observacao: os caracteres '[' e ']' so causam problema no Revit 2026. " +
                    "Nesta versao eles sao validos e nao travam nada.\n\n" +
                    "Use a ferramenta no Revit 2026 (onde o rename funciona) ou apos migrar o template.");
                return Result.Cancelled;
            }

            if (!Confirmar(alvos))
            {
                return Result.Cancelled;
            }

            int renomeados = 0;
            var falhas = new StringBuilder();

            using (var t = new Transaction(doc, "Editor de Fiacao: remover [ ] dos nomes"))
            {
                t.Start();
                foreach (var r in alvos)
                {
                    try
                    {
                        r.Elemento.Name = r.Novo;
                        renomeados++;
                    }
                    catch (Exception ex)
                    {
                        // Nome duplicado, protegido, etc. Nao aborta o lote inteiro.
                        var msg = string.IsNullOrWhiteSpace(ex.Message) ? "(sem mensagem)" : ex.Message;
                        falhas.AppendLine($"- {r.Antigo}: [{ex.GetType().Name}] {msg}");
                    }
                }
                t.Commit();
            }

            var resumo = new StringBuilder($"Renomeados: {renomeados} de {alvos.Count}.");
            if (falhas.Length > 0)
            {
                resumo.Append("\n\nNao foi possivel renomear:\n").Append(falhas);
            }
            TaskDialog.Show(Titulo, resumo.ToString());
            return Result.Succeeded;
        }

        private static bool Confirmar(List<Rename> alvos)
        {
            var previa = new StringBuilder();
            foreach (var r in alvos.Take(40))
            {
                previa.AppendLine(r.Antigo);
                previa.AppendLine("   -> " + r.Novo);
                previa.AppendLine();
            }
            if (alvos.Count > 40)
            {
                previa.AppendLine($"... e mais {alvos.Count - 40}.");
            }

            var dialog = new TaskDialog(Titulo + " - remover [ ]")
            {
                MainInstruction = $"{alvos.Count} nome(s) serao renomeados. Confirmar?",
                MainContent = "Os caracteres '[' e ']' serao removidos dos nomes. " +
                              "As referencias dos tipos de fiacao NAO quebram (usam Id interno).",
                ExpandedContent = previa.ToString(),
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };

            return dialog.Show() == TaskDialogResult.Yes;
        }

        /// <summary>Par (elemento, nome antigo, nome novo sem colchetes).</summary>
        private sealed class Rename
        {
            public Element Elemento { get; }
            public string Antigo { get; }
            public string Novo { get; }

            public Rename(Element elemento, string antigo)
            {
                Elemento = elemento;
                Antigo = antigo ?? string.Empty;
                Novo = Antigo.Replace("[", string.Empty).Replace("]", string.Empty);
            }
        }
    }
}
