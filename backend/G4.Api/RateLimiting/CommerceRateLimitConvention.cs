using G4.WebCommon;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.RateLimiting;

namespace G4.Api.RateLimiting;

public sealed class CommerceRateLimitConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        foreach (var action in controller.Actions)
        {
            // Machine-to-machine carrier calls are not charged against a buyer/seller quota.
            if (controller.ControllerName == "CarrierSimulator")
                continue;
            foreach (var selector in action.Selectors)
            {
                var methods = selector
                    .ActionConstraints.OfType<HttpMethodActionConstraint>()
                    .SelectMany(x => x.HttpMethods)
                    .ToArray();
                var group = Group(controller.ControllerName, action.ActionName, methods);
                selector.EndpointMetadata.Add(new EnableRateLimitingAttribute(group));
            }
        }
    }

    private static string Group(string controller, string action, string[] methods)
    {
        if (controller == "Authentication" && action == "Login")
            return CommerceRateLimiting.Login;
        if (
            controller == "SellerRegistration" && action == "Register"
            || controller == "Authentication" && action == "RegisterBuyer"
        )
            return CommerceRateLimiting.Registration;
        if (methods.Length > 0 && methods.All(x => x is "GET" or "HEAD" or "OPTIONS"))
            return CommerceRateLimiting.Read;
        if (controller == "Payments")
            return action == "Refund" ? CommerceRateLimiting.Finance : CommerceRateLimiting.Payment;
        if (
            controller == "PayPalCart"
            || controller == "ImmediateCheckout"
            || controller == "Cart" && action == "Checkout"
            || controller == "Checkout" && action == "CreateOrder"
        )
            return CommerceRateLimiting.Payment;
        if (
            controller == "SellerDebt" && action == "Repay"
            || controller == "Finance" && action is "RequestPayout" or "ResolveHold"
            || controller == "Disputes" && action is "Resolution" or "SubmitResponse"
            || controller == "Returns"
                && action is "ApproveReturn" or "ApproveWithCarrierFailure" or "ReceiveReturn"
            || controller == "Orders" && action == "DecideCancel"
        )
            return CommerceRateLimiting.Finance;
        return CommerceRateLimiting.Write;
    }
}
