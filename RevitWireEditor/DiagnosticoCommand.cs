#region Namespaces
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
#endregion

namespace RevitWireEditor
{
    /// <summary>
    /// Comando de inspecao (dev). No Revit 2026, para cada classe do modelo de condutor/cabo,
    /// obtem o objeto wrapper (via GetXxx(doc,id)) e dumpa TODAS as propriedades de instancia
    /// (com valor) + quais sao gravaveis. Referencias ElementId sao resolvidas por nome. Serve
    /// para congelar o padrao no plugin (abordagem B). Escreve um relatorio de texto.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class DiagnosticoCommand : IExternalCommand
    {
        private static readonly string[] ClassesAlvo =
        {
            "ConductorMaterial", "ConductorSize",
            "InsulationMaterial", "TemperatureRating",
            "CableType", "CableSize"
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            var sb = new StringBuilder();

            sb.AppendLine("=== RevitWireEditor - Dump completo (propriedades) da config de condutor/cabo ===");
            sb.AppendLine("Documento: " + doc.Title);
#if CABLESAPI
            sb.AppendLine();
            foreach (var nome in ClassesAlvo) DumpClasse(sb, doc, nome);
            DumpUsable(sb, doc);
            Experimentos(sb, doc);
#else
            sb.AppendLine("(Dump completo so implementado para Revit 2026+.)");
#endif

            string caminho = Path.Combine(Path.GetTempPath(), "RevitWireEditor-config-dump.txt");
            try { File.WriteAllText(caminho, sb.ToString(), Encoding.UTF8); }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Diagnostico", "Falha ao escrever o relatorio:\n" + ex.Message);
                return Result.Failed;
            }

            TaskDialog.Show("Diagnostico", "Relatorio gerado em:\n\n" + caminho + "\n\nPode fechar.");
            return Result.Succeeded;
        }

#if CABLESAPI
        private static void DumpClasse(StringBuilder sb, Document doc, string nomeClasse)
        {
            var asm = typeof(ConductorMaterial).Assembly;
            var tipo = asm.GetType("Autodesk.Revit.DB.Electrical." + nomeClasse);

            sb.AppendLine("############################################################");
            sb.AppendLine("### " + nomeClasse + (tipo == null ? "  (NAO ENCONTRADA)" : ""));
            sb.AppendLine("############################################################");
            if (tipo == null) { sb.AppendLine(); return; }

            // Propriedades de instancia (nome, tipo, gravavel?)
            var props = tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .Where(p => p.GetIndexParameters().Length == 0)
                            .OrderBy(p => p.Name)
                            .ToList();
            sb.AppendLine("  PROPRIEDADES (W=gravavel): " + string.Join(", ",
                props.Select(p => p.Name + (p.CanWrite ? "(W)" : "") + ":" + p.PropertyType.Name)));

            // Enumerador GetXxxIds(Document) e acessor GetXxx(Document, ElementId) -> tipo
            var getIds = tipo.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m =>
            {
                var ps = m.GetParameters();
                return ps.Length == 1 && typeof(Document).IsAssignableFrom(ps[0].ParameterType)
                    && typeof(IEnumerable).IsAssignableFrom(m.ReturnType) && m.Name.StartsWith("Get");
            });
            var getOne = tipo.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m =>
            {
                var ps = m.GetParameters();
                return ps.Length == 2 && typeof(Document).IsAssignableFrom(ps[0].ParameterType)
                    && typeof(ElementId).IsAssignableFrom(ps[1].ParameterType) && m.ReturnType == tipo;
            });

            if (getIds == null) { sb.AppendLine("  (sem enumerador)"); sb.AppendLine(); return; }

            List<ElementId> ids;
            try { ids = ((IEnumerable)getIds.Invoke(null, new object[] { doc })).Cast<ElementId>().ToList(); }
            catch (Exception ex) { sb.AppendLine("  enum falhou: " + (ex.InnerException?.Message ?? ex.Message)); sb.AppendLine(); return; }

            sb.AppendLine("  TOTAL: " + ids.Count);

            foreach (var id in ids)
            {
                var backing = doc.GetElement(id);
                object wrapper = null;
                if (getOne != null)
                {
                    try { wrapper = getOne.Invoke(null, new object[] { doc, id }); } catch { }
                }
                // CableType e um Element: o proprio elemento e o "wrapper".
                if (wrapper == null && typeof(Element).IsAssignableFrom(tipo)) wrapper = backing;
                sb.AppendLine();
                sb.AppendLine("  - " + (backing != null ? backing.Name : "(id " + id + ")") + "   [id " + id + "]");

                if (wrapper == null) { sb.AppendLine("      (wrapper nao obtido)"); continue; }

                foreach (var p in props)
                {
                    if (!p.CanRead) continue;
                    string val;
                    try { val = FormatValor(doc, p.GetValue(wrapper)); }
                    catch (Exception ex) { val = "<erro: " + (ex.InnerException?.Message ?? ex.Message) + ">"; }
                    sb.AppendLine($"      {p.Name}{(p.CanWrite ? "(W)" : "")} = {val}");
                }
            }
            sb.AppendLine();
        }

        private static void DumpUsable(StringBuilder sb, Document doc)
        {
            sb.AppendLine("############################################################");
            sb.AppendLine("### CABLETYPE: metodos de instancia (Usable/CableSize) + mapeamento UTILIZAVEL");
            sb.AppendLine("############################################################");

            var t = typeof(CableType);
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                               .Where(m => m.Name.IndexOf("Usable", StringComparison.OrdinalIgnoreCase) >= 0
                                        || m.Name.IndexOf("CableSize", StringComparison.OrdinalIgnoreCase) >= 0)
                               .OrderBy(m => m.Name))
            {
                var ps = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
                sb.AppendLine($"   {m.ReturnType.Name} {m.Name}({ps})");
            }
            sb.AppendLine();

            var getUsable = t.GetMethod("GetUsableCableSizeIds", Type.EmptyTypes);
            foreach (var id in CableType.GetCableTypeIds(doc))
            {
                var ct = doc.GetElement(id) as CableType;
                if (ct == null) continue;
                string linha = "  " + ct.Name + " -> ";
                try
                {
                    var usable = getUsable != null ? getUsable.Invoke(ct, null) as IEnumerable : null;
                    if (usable != null)
                    {
                        var names = usable.Cast<ElementId>().Select(i => doc.GetElement(i)?.Name);
                        linha += "[" + string.Join(" | ", names) + "]";
                    }
                    else linha += "(GetUsableCableSizeIds nao parametrless)";
                }
                catch (Exception ex) { linha += "erro: " + (ex.InnerException?.Message ?? ex.Message); }
                sb.AppendLine(linha);
            }
            sb.AppendLine();
        }

        private static void Experimentos(StringBuilder sb, Document doc)
        {
            sb.AppendLine("############################################################");
            sb.AppendLine("### EXPERIMENTOS DE CRIACAO (transacao REVERTIDA - nada e salvo)");
            sb.AppendLine("############################################################");

            RunRollback(doc, "criar-ConductorMaterial", sb, () =>
            {
                var cm = ConductorMaterial.Create(doc);
                cm.Name = "__RWE_MAT__";
                return $"OK criou material id {cm.Id} nome '{cm.Name}'";
            });

            RunRollback(doc, "criar-ConductorSize", sb, () =>
            {
                var cs = ConductorSize.Create(doc);
                cs.Name = "__RWE_SIZE__";
                cs.Diameter = 0.005;
                return $"OK criou size id {cs.Id} nome '{cs.Name}' diam {cs.Diameter}";
            });

            RunRollback(doc, "criar-CableType+refs", sb, () =>
            {
                var ct = CableType.Create(doc);
                ct.Name = "__RWE_CABLE__";
                var matId = ConductorMaterial.GetConductorMaterialIdByName(doc, "Cobre");
                if (matId != null && matId != ElementId.InvalidElementId) ct.ConductorMaterial = matId;
                // Reusa o CoreType de um cable type existente, se houver.
                var existentes = CableType.GetCableTypeIds(doc).Select(i => doc.GetElement(i) as CableType).Where(x => x != null).ToList();
                string core = "n/a";
                var modelo = existentes.FirstOrDefault(x => x.Id != ct.Id);
                if (modelo != null) { ct.CoreType = modelo.CoreType; core = ct.CoreType.ToString(); }
                return $"OK criou cabletype id {ct.Id}; Material setado='{doc.GetElement(ct.ConductorMaterial)?.Name}'; CoreType='{core}'";
            });

            sb.AppendLine();
        }

        private static void RunRollback(Document doc, string nome, StringBuilder sb, Func<string> acao)
        {
            using (var t = new Transaction(doc, "RWE " + nome))
            {
                try { t.Start(); sb.AppendLine($"[{nome}] {acao()}"); }
                catch (Exception ex)
                {
                    string msg = string.IsNullOrWhiteSpace(ex.Message) ? "(sem mensagem)" : ex.Message;
                    sb.AppendLine($"[{nome}] FALHOU: [{ex.GetType().Name}] {msg}");
                }
                finally { if (t.HasStarted() && !t.HasEnded()) t.RollBack(); }
            }
        }

        private static string FormatValor(Document doc, object val)
        {
            if (val == null) return "null";
            if (val is ElementId eid)
            {
                if (eid == ElementId.InvalidElementId) return "<nenhum>";
                var el = doc.GetElement(eid);
                return (el != null ? "'" + el.Name + "'" : "?") + " (id " + eid + ")";
            }
            if (val is IEnumerable en && !(val is string))
            {
                var itens = en.Cast<object>().Take(20)
                    .Select(o => o is ElementId ie ? (doc.GetElement(ie)?.Name ?? ie.ToString()) : o?.ToString());
                return "[" + string.Join("; ", itens) + "]";
            }
            return val.ToString();
        }
#endif
    }
}
