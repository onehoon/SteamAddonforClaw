using System.Reflection;

namespace SteamInputAddonforClaw.MsiLiveUpdateHelper;

internal static class MsiLiveUpdateOemRequestReflection
{
    private static readonly string[] GetPostRequestParameterNames = ["product_title", "country_code", "type", "os"];
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static string CreateRequestJson(
        Func<object, string> serialize,
        string productTitle,
        string countryCode,
        string category,
        string operatingSystem)
    {
        if (serialize is null) throw new ArgumentNullException(nameof(serialize));

        return serialize(new
        {
            product_title = productTitle,
            country_code = countryCode,
            type = category,
            os = operatingSystem
        });
    }

    internal static void ValidateGetPostRequest(MethodInfo requestMethod)
    {
        if (requestMethod is null) throw new ArgumentNullException(nameof(requestMethod));
        var parameters = requestMethod.GetParameters();
        if (parameters.Length != GetPostRequestParameterNames.Length)
            throw new MissingMethodException("The MSI catalog request method has an unexpected parameter count.");

        for (var index = 0; index < parameters.Length; index++)
        {
            if (parameters[index].ParameterType != typeof(string)
                || !string.Equals(parameters[index].Name, GetPostRequestParameterNames[index], StringComparison.Ordinal))
                throw new MissingMethodException("The MSI catalog request method has an unexpected parameter contract.");
        }
    }

    internal static object? InvokeGetPostRequest(
        MethodInfo requestMethod,
        object api,
        string productTitle,
        string countryCode,
        string category,
        string operatingSystem)
    {
        ValidateGetPostRequest(requestMethod);
        return requestMethod.Invoke(api, [productTitle, countryCode, category, operatingSystem]);
    }

    internal static void SetRequestJsonData(object api, string requestJson)
    {
        if (api is null) throw new ArgumentNullException(nameof(api));
        if (requestJson is null) throw new ArgumentNullException(nameof(requestJson));

        var apiType = api.GetType();
        var apiRequestProperty = apiType.GetProperty("AwsApiGatewayRequest", InstanceMembers);
        var apiRequest = apiRequestProperty?.GetValue(api)
            ?? apiType.GetField("AwsApiGatewayRequest", InstanceMembers)?.GetValue(api)
            ?? throw new MissingMemberException(apiType.FullName, "AwsApiGatewayRequest");

        var requestType = apiRequest.GetType();
        var jsonDataProperty = requestType.GetProperty("JsonData", InstanceMembers);
        if (jsonDataProperty is not null)
        {
            if (jsonDataProperty.PropertyType != typeof(string) || jsonDataProperty.GetSetMethod(nonPublic: true) is null)
                throw new MissingMemberException(requestType.FullName, "Writable JsonData property");
            jsonDataProperty.SetValue(apiRequest, requestJson);
            return;
        }

        var jsonDataField = requestType.GetField("JsonData", InstanceMembers);
        if (jsonDataField is null || jsonDataField.FieldType != typeof(string) || jsonDataField.IsInitOnly)
            throw new MissingMemberException(requestType.FullName, "Writable JsonData member");
        jsonDataField.SetValue(apiRequest, requestJson);
    }
}
