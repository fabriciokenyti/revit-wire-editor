#region Namespaces
using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
#if CABLESAPI
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Autodesk.Revit.DB.Electrical;
#endif
#endregion

namespace RevitWireEditor
{
    /// <summary>
    /// "Preparar Config" (Revit 2026+): deixa a config de condutores/cabos EXATAMENTE igual ao
    /// padrao embutido (seed). Garante que cada item do padrao exista (cria o que falta) e
    /// EXCLUI tudo que estiver fora do padrao. Substitui o fluxo manual de
    /// limpar + excluir 1-a-1 + transferir normas.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class PrepararConfigCommand : IExternalCommand
    {
        private const string Titulo = "Preparar Config (padrao de cabos)";

#if !CABLESAPI
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            TaskDialog.Show(Titulo, "Disponivel apenas no Revit 2026+ (usa a API nova de condutores/cabos).");
            return Result.Cancelled;
        }
#else
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            Seed seed;
            try { seed = CarregarSeed(); }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show(Titulo, "Falha ao ler o padrao embutido:\n" + ex.Message);
                return Result.Failed;
            }

            IntPtr hwnd = commandData.Application.MainWindowHandle;

            // Previa (somente leitura): quantos criar / excluir.
            var criar = ContarCriacoes(doc, seed);
            var excluir = ContarExclusoes(doc, seed);
            int totalCriar = criar.mat + criar.ins + criar.temp + criar.csize + criar.ctype + criar.cablesize;
            int totalExcluir = excluir.mat + excluir.ins + excluir.temp + excluir.csize + excluir.ctype + excluir.cablesize;

            if (totalCriar == 0 && totalExcluir == 0)
            {
                // Ja esta no padrao: reaplicamos so o "utilizavel" (barato) e avisamos.
                AplicarSomenteUtilizavel(doc, seed);
                EmDialog.Show("Configuração de fiação",
                    "A configuração de condutores e cabos já está no padrão da Engenharia Moderna.\n" +
                    "Nada a criar ou excluir.",
                    "Fechar", null, EmVariant.Info, hwnd);
                return Result.Succeeded;
            }

            var confirmar = EmDialog.Show("Configurar fiação",
                "Vou deixar a configuração de condutores e cabos exatamente no padrão da Engenharia Moderna.\n" +
                "\n" +
                $"Criar (o que falta):  {totalCriar} itens\n" +
                $"Excluir (fora do padrão):  {totalExcluir} itens\n" +
                "\n" +
                "É uma ação única — dá para desfazer com Ctrl+Z.",
                "Aplicar padrão", "Cancelar", EmVariant.Warning, hwnd);
            if (confirmar != EmResult.Primary) return Result.Cancelled;

            int excluidos;
            using (var t = new Transaction(doc, "Configurar fiação: aplicar padrão"))
            {
                t.Start();
                try
                {
                    GarantirLeafs(doc, seed);        // materiais, isolantes, temperaturas, tamanhos de condutor
                    GarantirCableSizes(doc, seed);   // antes dos tipos (o "utilizavel" precisa dos tamanhos)
                    GarantirCableTypes(doc, seed);   // tipos + marca utilizavel
                    excluidos = ExcluirForaDoPadrao(doc, seed);
                    t.Commit();
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    message = ex.Message;
                    EmDialog.Show("Não foi possível configurar",
                        "Ocorreu um erro e nada foi alterado (revertido com segurança):\n\n" + ex.Message,
                        "Fechar", null, EmVariant.Error, hwnd);
                    return Result.Failed;
                }
            }

            EmDialog.Show("Fiação configurada",
                "A configuração de condutores e cabos está no padrão da Engenharia Moderna.\n" +
                "\n" +
                $"Materiais de condutor:  {seed.ConductorMaterials.Count}\n" +
                $"Materiais isolantes:  {seed.InsulationMaterials.Count}\n" +
                $"Classificações de temperatura:  {seed.TemperatureRatings.Count}\n" +
                $"Tamanhos de condutor:  {seed.ConductorSizes.Count}\n" +
                $"Tipos de cabo:  {seed.CableTypes.Count}  (com utilizável)\n" +
                $"Tamanhos de cabo:  {seed.CableSizes.Count}\n" +
                $"\nItens fora do padrão removidos:  {excluidos}",
                "Fechar", null, EmVariant.Success, hwnd);
            return Result.Succeeded;
        }

        // ---------- Seed ----------

        private static Seed CarregarSeed()
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream("RevitWireEditor.Resources.config-seed.json"))
            {
                if (s == null) throw new InvalidOperationException("recurso config-seed.json nao encontrado no assembly.");
                var opt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<Seed>(s, opt);
            }
        }

        // ---------- Criacao (idempotente por nome) ----------

        private static void GarantirLeafs(Document doc, Seed seed)
        {
            foreach (var m in seed.ConductorMaterials)
                if (Invalido(ConductorMaterial.GetConductorMaterialIdByName(doc, m)))
                    ConductorMaterial.Create(doc).Name = m;

            foreach (var i in seed.InsulationMaterials)
                if (Invalido(InsulationMaterial.GetInsulationMaterialIdByName(doc, i)))
                    InsulationMaterial.Create(doc).Name = i;

            foreach (var tr in seed.TemperatureRatings)
                if (Invalido(TemperatureRating.GetTemperatureRatingIdByName(doc, tr)))
                    TemperatureRating.Create(doc).Name = tr;

            foreach (var cs in seed.ConductorSizes)
            {
                var id = ConductorSize.GetConductorSizeIdByName(doc, cs.Name);
                ConductorSize wrap = Invalido(id)
                    ? ConductorSize.Create(doc)
                    : ConductorSize.GetConductorSize(doc, id);
                if (Invalido(id)) wrap.Name = cs.Name;
                wrap.Diameter = cs.Diameter;
            }
        }

        private static void GarantirCableTypes(Document doc, Seed seed)
        {
            foreach (var def in seed.CableTypes)
            {
                var ct = AcharCableType(doc, def.Name) ?? CableType.Create(doc);
                if (ct.Name != def.Name) ct.Name = def.Name;

                var mat = ConductorMaterial.GetConductorMaterialIdByName(doc, def.ConductorMaterial);
                if (!Invalido(mat)) ct.ConductorMaterial = mat;

                var ins = InsulationMaterial.GetInsulationMaterialIdByName(doc, def.InsulationMaterial);
                if (!Invalido(ins)) ct.InsulationMaterial = ins;

                if (!string.IsNullOrEmpty(def.TemperatureRating))
                {
                    var tr = TemperatureRating.GetTemperatureRatingIdByName(doc, def.TemperatureRating);
                    if (!Invalido(tr)) ct.TemperatureRating = tr;
                }

                if (!string.IsNullOrEmpty(def.CoreType)
                    && Enum.TryParse(def.CoreType, out CoreType core))
                    ct.CoreType = core;

                MarcarUtilizavel(doc, seed, ct, def);
            }
        }

        /// <summary>
        /// Marca "Utilizavel" exatamente conforme o padrao: percorre TODOS os tamanhos de cabo
        /// e liga/desliga cada um no tipo (evita depender do default do Revit).
        /// </summary>
        private static void MarcarUtilizavel(Document doc, Seed seed, CableType ct, CableTypeDef def)
        {
            var usaveis = new HashSet<string>(def.UsableCableSizes ?? new List<string>());
            foreach (var csDef in seed.CableSizes)
            {
                var sizeId = CableSize.GetCableSizeIdByName(doc, csDef.Name);
                if (Invalido(sizeId)) continue;
                ct.SetCableSizeUsable(sizeId, usaveis.Contains(csDef.Name));
            }
        }

        /// <summary>Caso "ja no padrao": reaplica so o mapeamento utilizavel, numa transacao propria.</summary>
        private static void AplicarSomenteUtilizavel(Document doc, Seed seed)
        {
            using (var t = new Transaction(doc, "Configurar fiação: reaplicar utilizável"))
            {
                t.Start();
                foreach (var def in seed.CableTypes)
                {
                    var ct = AcharCableType(doc, def.Name);
                    if (ct != null) MarcarUtilizavel(doc, seed, ct, def);
                }
                t.Commit();
            }
        }

        private static void GarantirCableSizes(Document doc, Seed seed)
        {
            foreach (var def in seed.CableSizes)
            {
                var id = CableSize.GetCableSizeIdByName(doc, def.Name);
                CableSize cz = Invalido(id) ? CableSize.Create(doc) : CableSize.GetCableSize(doc, id);
                if (Invalido(id)) cz.Name = def.Name;

                cz.Comments = def.Comments ?? string.Empty;
                cz.NumberOfHotConductors = def.NHot;
                cz.NumberOfNeutralConductors = def.NNeutral;
                cz.NumberOfGroundConductors = def.NGround;
                cz.NumberOfOtherConductors = def.NOther;

                SetSizeRef(doc, def.Hot, v => cz.HotConductorSize = v);
                SetSizeRef(doc, def.Neutral, v => cz.NeutralConductorSize = v);
                SetSizeRef(doc, def.Ground, v => cz.GroundConductorSize = v);
                SetSizeRef(doc, def.Other, v => cz.OtherConductorSize = v);
            }
        }

        private static void SetSizeRef(Document doc, string nome, Action<ElementId> set)
        {
            if (string.IsNullOrEmpty(nome)) return;
            var id = ConductorSize.GetConductorSizeIdByName(doc, nome);
            if (!Invalido(id)) set(id);
        }

        // ---------- Exclusao do que esta fora do padrao ----------

        private static int ExcluirForaDoPadrao(Document doc, Seed seed)
        {
            int d = 0;
            d += ExcluirForaDe(doc, CableSize.GetCableSizeIds(doc), new HashSet<string>(seed.CableSizes.Select(x => x.Name)));
            d += ExcluirForaDe(doc, CableType.GetCableTypeIds(doc), new HashSet<string>(seed.CableTypes.Select(x => x.Name)));
            d += ExcluirForaDe(doc, ConductorSize.GetConductorSizeIds(doc), new HashSet<string>(seed.ConductorSizes.Select(x => x.Name)));
            d += ExcluirForaDe(doc, ConductorMaterial.GetConductorMaterialIds(doc), new HashSet<string>(seed.ConductorMaterials));
            d += ExcluirForaDe(doc, InsulationMaterial.GetInsulationMaterialIds(doc), new HashSet<string>(seed.InsulationMaterials));
            d += ExcluirForaDe(doc, TemperatureRating.GetTemperatureRatingIds(doc), new HashSet<string>(seed.TemperatureRatings));
            return d;
        }

        private static int ExcluirForaDe(Document doc, IList<ElementId> ids, HashSet<string> manter)
        {
            var apagar = ids.Where(id =>
            {
                var name = doc.GetElement(id)?.Name;
                return name != null && !manter.Contains(name);
            }).ToList();

            int total = 0;
            foreach (var id in apagar)
            {
                try { total += doc.Delete(id).Count; }
                catch { /* em uso / minimo obrigatorio: ignora */ }
            }
            return total;
        }

        // ---------- Previa (contagens, sem mutar) ----------

        private static (int mat, int ins, int temp, int csize, int ctype, int cablesize)
            ContarCriacoes(Document doc, Seed seed)
        {
            int mat = seed.ConductorMaterials.Count(m => Invalido(ConductorMaterial.GetConductorMaterialIdByName(doc, m)));
            int ins = seed.InsulationMaterials.Count(i => Invalido(InsulationMaterial.GetInsulationMaterialIdByName(doc, i)));
            int temp = seed.TemperatureRatings.Count(t => Invalido(TemperatureRating.GetTemperatureRatingIdByName(doc, t)));
            int csize = seed.ConductorSizes.Count(s => Invalido(ConductorSize.GetConductorSizeIdByName(doc, s.Name)));
            int ctype = seed.CableTypes.Count(c => AcharCableType(doc, c.Name) == null);
            int cablesize = seed.CableSizes.Count(s => Invalido(CableSize.GetCableSizeIdByName(doc, s.Name)));
            return (mat, ins, temp, csize, ctype, cablesize);
        }

        private static (int mat, int ins, int temp, int csize, int ctype, int cablesize)
            ContarExclusoes(Document doc, Seed seed)
        {
            int Fora(IList<ElementId> ids, HashSet<string> manter) =>
                ids.Count(id => { var n = doc.GetElement(id)?.Name; return n != null && !manter.Contains(n); });

            return (
                Fora(ConductorMaterial.GetConductorMaterialIds(doc), new HashSet<string>(seed.ConductorMaterials)),
                Fora(InsulationMaterial.GetInsulationMaterialIds(doc), new HashSet<string>(seed.InsulationMaterials)),
                Fora(TemperatureRating.GetTemperatureRatingIds(doc), new HashSet<string>(seed.TemperatureRatings)),
                Fora(ConductorSize.GetConductorSizeIds(doc), new HashSet<string>(seed.ConductorSizes.Select(x => x.Name))),
                Fora(CableType.GetCableTypeIds(doc), new HashSet<string>(seed.CableTypes.Select(x => x.Name))),
                Fora(CableSize.GetCableSizeIds(doc), new HashSet<string>(seed.CableSizes.Select(x => x.Name)))
            );
        }

        // ---------- Utilitarios ----------

        private static bool Invalido(ElementId id) => id == null || id == ElementId.InvalidElementId;

        private static CableType AcharCableType(Document doc, string nome)
        {
            return CableType.GetCableTypeIds(doc)
                .Select(id => doc.GetElement(id) as CableType)
                .FirstOrDefault(ct => ct != null && ct.Name == nome);
        }

        // ---------- POCOs do seed ----------

        private sealed class Seed
        {
            public List<string> ConductorMaterials { get; set; } = new List<string>();
            public List<string> InsulationMaterials { get; set; } = new List<string>();
            public List<string> TemperatureRatings { get; set; } = new List<string>();
            public List<SizeDef> ConductorSizes { get; set; } = new List<SizeDef>();
            public List<CableTypeDef> CableTypes { get; set; } = new List<CableTypeDef>();
            public List<CableSizeDef> CableSizes { get; set; } = new List<CableSizeDef>();
        }

        private sealed class SizeDef
        {
            public string Name { get; set; }
            public double Diameter { get; set; }
        }

        private sealed class CableTypeDef
        {
            public string Name { get; set; }
            public string ConductorMaterial { get; set; }
            public string InsulationMaterial { get; set; }
            public string TemperatureRating { get; set; }
            public string CoreType { get; set; }
            public List<string> UsableCableSizes { get; set; } = new List<string>();
        }

        private sealed class CableSizeDef
        {
            public string Name { get; set; }
            public string Comments { get; set; }
            public string Hot { get; set; }
            public string Neutral { get; set; }
            public string Ground { get; set; }
            public string Other { get; set; }
            public int NHot { get; set; }
            public int NNeutral { get; set; }
            public int NGround { get; set; }
            public int NOther { get; set; }
        }
#endif
    }
}
