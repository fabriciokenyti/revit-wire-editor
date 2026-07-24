#region Namespaces
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
#endregion

namespace RevitWireEditor
{
    /// <summary>
    /// Ponto de entrada. Cria o painel "Editor de Fiacao" na aba Complementos com um botao
    /// unico "Configurar Fiacao" (aplica o padrao de condutores/cabos da Engenharia Moderna).
    /// </summary>
    internal class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication a)
        {
            RibbonPanel panel = a.CreateRibbonPanel("Editor de Fiacao");

            var btn = new PushButtonData(
                "cmdConfigurarFiacao",
                "Configurar\nFiação",
                Assembly.GetExecutingAssembly().Location,
                "RevitWireEditor.PrepararConfigCommand")
            {
                ToolTip = "Aplica o padrao de condutores e cabos da Engenharia Moderna: cria o que falta " +
                          "e remove o que estiver fora do padrao (Revit 2026+).",
                LongDescription = "Substitui o fluxo manual de limpar + excluir 1-a-1 + transferir normas de projeto. " +
                                  "Acao unica, reversivel com Ctrl+Z."
            };
            btn.LargeImage = LoadIcon(32);
            btn.Image = LoadIcon(16);

            panel.AddItem(btn);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication a) => Result.Succeeded;

        /// <summary>Carrega o icone embutido normalizado para o tamanho pedido (evita sumico por DPI, L-35).</summary>
        private static BitmapImage LoadIcon(int px)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream("RevitWireEditor.Resources.icon-fiacao.png"))
            {
                if (s == null) return null;
                var img = new BitmapImage();
                img.BeginInit();
                img.StreamSource = s;
                img.DecodePixelWidth = px;
                img.DecodePixelHeight = px;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                return img;
            }
        }
    }
}
