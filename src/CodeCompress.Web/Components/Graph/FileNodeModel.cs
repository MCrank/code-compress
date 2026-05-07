using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using CodeCompress.Web.Services.Graph;

namespace CodeCompress.Web.Components.Graph;

public sealed class FileNodeModel : NodeModel
{
    public FileNodeModel(FileNodeViewModel viewModel, Point position)
        : base(position)
    {
        ViewModel = viewModel;
        Size = new Size(220, 80);
    }

    public FileNodeViewModel ViewModel { get; }
}
