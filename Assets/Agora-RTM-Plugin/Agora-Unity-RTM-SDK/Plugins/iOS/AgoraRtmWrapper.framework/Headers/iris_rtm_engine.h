#pragma once

#include "iris_base.h"
#include "iris_engine_base.h"
#include "iris_platform.h"

IRIS_API IApiEngineBase *IRIS_CALL createIrisRtmClient(IrisHandle client);

IRIS_API void IRIS_CALL destroyIrisRtmClient(IApiEngineBase *engine);

IRIS_API const char *IRIS_CALL getIrisRtmErrorReason(int error_code);
