using DotVVM.Framework.Controls.Infrastructure;
using DotVVM.Framework.Hosting;
using DotVVM.Framework.Runtime.Filters;

namespace DotVVM.Framework.ViewModel.Serialization
{
    public interface IViewModelSerializer
    {
        void BuildViewModel(IDotvvmRequestContext context);

        string BuildStaticCommandResponse(IDotvvmRequestContext context, object result);

        string SerializeViewModel(IDotvvmRequestContext context);
        
        string SerializeModelState(IDotvvmRequestContext context);

        void PopulateViewModel(IDotvvmRequestContext context, string serializedPostData);

        ActionInfo ResolveCommand(IDotvvmRequestContext context, DotvvmView view);

        void AddPostBackUpdatedControls(IDotvvmRequestContext context);
    }
}