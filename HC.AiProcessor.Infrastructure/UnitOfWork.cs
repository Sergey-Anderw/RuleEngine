using HC.Packages.Common.Contracts.V1;
using HC.Packages.Persistent.Cap;
using HC.Packages.Persistent.Infrastructure;

namespace HC.AiProcessor.Infrastructure;

internal sealed class UnitOfWork(
    IIdentityContextUser identityContextUser,
    DataContextProvider context,
    ILogger<UnitOfWork> logger,
    ISystemClock systemClock,
    IEventCollector eventCollector,
    ICapPublisherWrapper capPublisherWrapper)
    : HC.Packages.Persistent.Infrastructure.UnitOfWork(
        identityContextUser,
        context,
        logger,
        systemClock,
        eventCollector,
        capPublisherWrapper);
