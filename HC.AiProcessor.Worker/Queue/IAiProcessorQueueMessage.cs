using HC.AiProcessor.Worker.Queue.Handlers;

namespace HC.AiProcessor.Worker.Queue;

[JsonDerivedType(
    derivedType: typeof(ExternalBatchGenerateMessage),
    typeDiscriminator: nameof(ExternalBatchGenerateMessage))]
[JsonDerivedType(
    derivedType: typeof(ExternalBatchPopulateAttributesMessage),
    typeDiscriminator: nameof(ExternalBatchPopulateAttributesMessage))]
[JsonDerivedType(
    derivedType: typeof(ExternalBatchRephraseMessage),
    typeDiscriminator: nameof(ExternalBatchRephraseMessage))]
[JsonDerivedType(
    derivedType: typeof(ExternalBatchTranslateMessage),
    typeDiscriminator: nameof(ExternalBatchTranslateMessage))]
[JsonDerivedType(
    derivedType: typeof(DeleteAiProductAttributeEmbeddingsMessage),
    typeDiscriminator: nameof(DeleteAiProductAttributeEmbeddingsMessage))]
[JsonDerivedType(
    derivedType: typeof(DeleteAiProductsMessage),
    typeDiscriminator: nameof(DeleteAiProductsMessage))]
[JsonDerivedType(
    derivedType: typeof(ExternalDetermineProductFamilyMessage),
    typeDiscriminator: nameof(ExternalDetermineProductFamilyMessage))]
[JsonDerivedType(
    derivedType: typeof(ExternalGenerateMessage),
    typeDiscriminator: nameof(ExternalGenerateMessage))]
[JsonDerivedType(
    derivedType: typeof(ExternalPopulateAttributesMessage),
    typeDiscriminator: nameof(ExternalPopulateAttributesMessage))]
[JsonDerivedType(
    derivedType: typeof(ExternalRephraseMessage),
    typeDiscriminator: nameof(ExternalRephraseMessage))]
[JsonDerivedType(
    derivedType: typeof(ExternalTranslateMessage),
    typeDiscriminator: nameof(ExternalTranslateMessage))]
[JsonDerivedType(
    derivedType: typeof(GenerateAiProductAttributeEmbeddingsMessage),
    typeDiscriminator: nameof(GenerateAiProductAttributeEmbeddingsMessage))]
[JsonDerivedType(
    derivedType: typeof(RefreshAiProductsMessage),
    typeDiscriminator: nameof(RefreshAiProductsMessage))]
public interface IAiProcessorQueueMessage : IRequest;
