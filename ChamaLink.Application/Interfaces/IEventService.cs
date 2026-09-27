using ChamaLink.Application.DTOs;

namespace ChamaLink.Application.Interfaces;

public interface IEventService
{
    Task<EventResponseDto> TriggerEventAsync(TriggerEventDto dto);
}