import { HubConnectionBuilder, HubConnection, HubConnectionState, LogLevel } from '@microsoft/signalr';

export function createShowtimeHubConnection(): HubConnection {
  return new HubConnectionBuilder()
    .withUrl('/hubs/showtimes')
    .withAutomaticReconnect([0, 2000, 5000, 10000, 15000])
    .configureLogging(LogLevel.Warning)
    .build();
}

export async function joinShowtime(connection: HubConnection, showtimeId: string): Promise<void> {
  if (connection.state === HubConnectionState.Connected) {
    await connection.invoke('JoinShowtime', showtimeId);
  }
}

export async function leaveShowtime(connection: HubConnection, showtimeId: string): Promise<void> {
  if (connection.state === HubConnectionState.Connected) {
    try {
      await connection.invoke('LeaveShowtime', showtimeId);
    } catch {
      // ignore
    }
  }
}
