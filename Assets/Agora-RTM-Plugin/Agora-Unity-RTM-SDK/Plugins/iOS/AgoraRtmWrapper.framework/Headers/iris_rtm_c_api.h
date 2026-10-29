//
// Agora Iris Rtm Engine SDK
// This is not a thread-safe library, do not call functions in multithread.
//

#ifndef __IRIS_RTM_C_API_H__
#define __IRIS_RTM_C_API_H__

#include "iris_base.h"

/**
 * @brief Create an IrisRtmEngine object and return the handle value of 
 * the object.
 * 
 * @param client The handle value of an exist RtmClient object.
 * @return
 * - zero value for failed.
 * - nonzero value for success. 
 */
IRIS_API IrisHandle IRIS_CALL CreateIrisRtmEngine(IrisHandle client);

/**
 * @brief Destroy an IrisRtmEngine object.
 * 
 * @param engine The handle value of IrisRtmEngine object. 
 */
IRIS_API void IRIS_CALL DestroyIrisRtmEngine(IrisHandle engine);

/**
 * @brief Call api function with specified IrisRtmEngine.
 * 
 * @param engine The handle value of an IrisRtmEngine
 * @param param The pointer to an ApiParam object.
 * @return
 * - 0: Success.
 * - < 0 : Failure. 
 */
IRIS_API int IRIS_CALL CallIrisRtmApi(IrisHandle engine, ApiParam *param);

/**
 * @brief Create an IrisEventHandler object.
 * 
 * @param handler 
 * @return The handle value of created event handler.
 */
IRIS_API IrisEventHandlerHandle IRIS_CALL
CreateIrisRtmEventHandler(const IrisCEventHandler *handler);

/**
 * @brief Destroy the IrisEventHandler object with specified handle value.
 * 
 * @param handler The handle value of an IrisEventHandler.
 */
IRIS_API void IRIS_CALL
DestroyIrisRtmEventHandler(IrisEventHandlerHandle handler);

/**
 * @brief Convert error code to error string.
 * 
 * @param error_code Received error code.
 * @return The error string.
*/
IRIS_API const char *IRIS_CALL GetIrisRtmErrorReason(int error_code);

/**
 * @brief Get the version info of the Agora RTM SDK.
 * 
 * @return The version info of the Agora RTM SDK.
*/
IRIS_API const char *IRIS_CALL GetIrisRtmVersion();

#endif