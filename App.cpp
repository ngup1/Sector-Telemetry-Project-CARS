/**
 * AMS2 Shared memory example project. Based on SMS PCARS2 sample.
 * Compatible with "Project Cars 2" in-game shared memory option.
 */

// Used for memory-mapped functionality
#include <windows.h>
#include "sharedmemory.h"

// Used for this example
#include <stdio.h>
#include <conio.h>

// Name of the pCars memory mapped file
#define MAP_OBJECT_NAME L"$pcars2$"

int main()
{
	// Open the memory-mapped file
	HANDLE fileHandle = OpenFileMapping( PAGE_READONLY, FALSE, MAP_OBJECT_NAME );
	if (fileHandle == NULL)
	{
		printf( "Could not open file mapping object (%d).\n", GetLastError() );
		return 1;
	}

	// Get the data structure
	const SharedMemory* sharedData = (SharedMemory*)MapViewOfFile( fileHandle, PAGE_READONLY, 0, 0, sizeof(SharedMemory) );
	SharedMemory* localCopy = new SharedMemory;
	if (sharedData == NULL)
	{
		printf( "Could not map view of file (%d).\n", GetLastError() );

		CloseHandle( fileHandle );
		return 1;
	}

	// Ensure we're sync'd to the correct data version
	if ( sharedData->mVersion != SHARED_MEMORY_VERSION )
	{
		printf( "Data version mismatch\n");
		return 1;
	}


	//------------------------------------------------------------------------------
	// TEST DISPLAY CODE
	//------------------------------------------------------------------------------
	static unsigned lastSeq = 0;
	unsigned int updateIndex(0);
	unsigned int indexChange(0);

	LARGE_INTEGER lastDisplayUpdate;
	QueryPerformanceCounter( &lastDisplayUpdate );

	printf( "ESC TO EXIT\n\n" );
	while (true)
	{
		if ( _kbhit() && _getch() == 27 ) // check for escape
		{
			break;
		}

		if ( sharedData->mSequenceNumber % 2 )
		{
			// Odd sequence number indicates, that write into the shared memory is just happening
			continue;
		}

		indexChange = sharedData->mSequenceNumber - updateIndex;
		updateIndex = sharedData->mSequenceNumber;

		//Copy the whole structure before processing it, otherwise the risk of the game writing into it during processing is too high.
		memcpy(localCopy,sharedData,sizeof(SharedMemory));


		if (localCopy->mSequenceNumber != updateIndex )
		{
			// More writes had happened during the read. Should be rare, but can happen.
			continue;
		}

		LARGE_INTEGER now;
		LARGE_INTEGER frequency;
		QueryPerformanceCounter( &now );
		QueryPerformanceFrequency( &frequency );

		double elapsedMilli = ((now.QuadPart - lastDisplayUpdate.QuadPart) * 1000.0) / frequency.QuadPart;

		// delay display update
		if (elapsedMilli < 300)
		{
			continue;
		}

		system("cls");

		printf( "Sequence number increase %d, current index %d, previous index %d\n", indexChange, localCopy->mSequenceNumber, updateIndex );

		const bool isValidParticipantIndex = localCopy->mViewedParticipantIndex != -1 && localCopy->mViewedParticipantIndex < localCopy->mNumParticipants && localCopy->mViewedParticipantIndex < STORED_PARTICIPANTS_MAX;
		if ( isValidParticipantIndex )
		{
			const ParticipantInfo& viewedParticipantInfo = localCopy->mParticipantInfo[sharedData->mViewedParticipantIndex];
			printf( "mParticipantName: (%s)\n", viewedParticipantInfo.mName );
			printf( "lap Distance = %f \n", viewedParticipantInfo.mCurrentLapDistance );
		}

		printf( "mGameState: (%d)\n", localCopy->mGameState );
		printf( "mSessionState: (%d)\t", localCopy->mSessionState );
		printf( "mEventTimeRemaining: (%f)\t", localCopy->mEventTimeRemaining );
		printf( "mLapsInEvent: (%d [+ %d Laps])\n\n", localCopy->mLapsInEvent, localCopy->mSessionAdditionalLaps );
		

		printf( "mOdometerKM: (%0.2f)\n\n", localCopy->mOdometerKM );


		// --------------------- TYRE TEMPS ---------------------------
		printf( "%-6s\t%-6s\t%-6s\t\t", "O", "M", "I" );
		printf( "%-6s\t%-6s\t%-6s\t\t\n\n", "I", "M", "O" );
		printf( "%-6.1f\t%-6.1f\t%-6.1f\t\t", localCopy->mTyreTempLeft[TYRE_FRONT_LEFT], localCopy->mTyreTempCenter[TYRE_FRONT_LEFT], localCopy->mTyreTempRight[TYRE_FRONT_LEFT] );
		printf( "%-6.1f\t%-6.1f\t%-6.1f\t\t\n\n", localCopy->mTyreTempLeft[TYRE_FRONT_RIGHT], localCopy->mTyreTempCenter[TYRE_FRONT_RIGHT], localCopy->mTyreTempRight[TYRE_FRONT_RIGHT] );
		printf( "%-6.1f\t%-6.1f\t%-6.1f\t\t", localCopy->mTyreTempLeft[TYRE_REAR_LEFT], localCopy->mTyreTempCenter[TYRE_REAR_LEFT], localCopy->mTyreTempRight[TYRE_REAR_LEFT] );
		printf( "%-6.1f\t%-6.1f\t%-6.1f\t\t\n\n", localCopy->mTyreTempLeft[TYRE_REAR_RIGHT], localCopy->mTyreTempCenter[TYRE_REAR_RIGHT], localCopy->mTyreTempRight[TYRE_REAR_RIGHT] );

		printf( "\n\n" );

		// --------------------- DRS 1 ---------------------------
		printf( "DRS  [ %d ] \n", localCopy->mDrsState);
		printf( "%-12s\t%-12s\t%-12s\t%-12s\t%-12s\t\t\n\n", "installed", "use zones", "triggered", "available", "active" );
		printf( "%-12d\t%-12d\t%-12d\t%-12d\t%-12d\t\t\n\n", 
				((localCopy->mDrsState & DRS_INSTALLED) == DRS_INSTALLED),
				((localCopy->mDrsState & DRS_ZONE_RULES) == DRS_ZONE_RULES),
				((localCopy->mDrsState & DRS_AVAILABLE_NEXT) == DRS_AVAILABLE_NEXT),
				((localCopy->mDrsState & DRS_AVAILABLE_NOW) == DRS_AVAILABLE_NOW),
				((localCopy->mDrsState & DRS_ACTIVE) == DRS_ACTIVE)
			);
		printf( "\n\n" );

		// --------------------- ERS ---------------------------

		printf( "ERS Mode: %d %s\n", localCopy->mErsDeploymentMode, localCopy->mErsAutoModeEnabled ? "(AUTO)" : "" );
		printf( "\n\n" );

		// --------------------- CLUTCH ---------------------------

		printf( "Clutch State --- Wear: %d%% Temp: %fC Overheat: %d Slip: %d \n", (int)localCopy->mClutchWear * 100, localCopy->mClutchTemp-273.16f, localCopy->mClutchOverheated, localCopy->mClutchSlipping );
		printf( "\n\n" );

		// --------------------- ABS ---------------------------

		printf( "ABS SETTING: %d \t\t TCS SETTING: %d", localCopy->mAntiLockSetting, localCopy->mTractionControlSetting );

		printf( "\n\n" );

		for (int i = 0; i < localCopy->mNumParticipants; ++i)
		{
			const ParticipantInfo& leaderPart = localCopy->mParticipantInfo[i];
			if (leaderPart.mRacePosition == 1)
			{
				printf( "mParticipantName: (%s)\n", leaderPart.mName );
				printf( "Race Status = %u \n", localCopy->mRaceStates[i] );
				printf( "current lap = %d \n", leaderPart.mCurrentLap );
				break;
			}
		}

		QueryPerformanceCounter( &lastDisplayUpdate );
	}
	//------------------------------------------------------------------------------

	// Cleanup
	UnmapViewOfFile( sharedData );
	CloseHandle( fileHandle );
	delete localCopy;

	return 0;
}
