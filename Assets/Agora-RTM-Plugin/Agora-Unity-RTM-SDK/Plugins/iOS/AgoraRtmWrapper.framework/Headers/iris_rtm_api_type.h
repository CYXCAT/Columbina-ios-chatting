#pragma once

// class IStreamChannel start
#define FUNC_STREAMCHANNEL_JOIN "StreamChannel_join"
#define FUNC_STREAMCHANNEL_RENEWTOKEN "StreamChannel_renewToken"
#define FUNC_STREAMCHANNEL_LEAVE "StreamChannel_leave"
#define FUNC_STREAMCHANNEL_GETCHANNELNAME "StreamChannel_getChannelName"
#define FUNC_STREAMCHANNEL_JOINTOPIC "StreamChannel_joinTopic"
#define FUNC_STREAMCHANNEL_PUBLISHTOPICMESSAGE                                 \
  "StreamChannel_publishTopicMessage"
#define FUNC_STREAMCHANNEL_LEAVETOPIC "StreamChannel_leaveTopic"
#define FUNC_STREAMCHANNEL_SUBSCRIBETOPIC "StreamChannel_subscribeTopic"
#define FUNC_STREAMCHANNEL_UNSUBSCRIBETOPIC "StreamChannel_unsubscribeTopic"
#define FUNC_STREAMCHANNEL_GETSUBSCRIBEDUSERLIST                               \
  "StreamChannel_getSubscribedUserList"
#define FUNC_STREAMCHANNEL_RELEASE "StreamChannel_release"
// class IStreamChannel end

// class IRtmClient start
#define FUNC_RTMCLIENT_INITIALIZE "RtmClient_initialize"
#define FUNC_RTMCLIENT_RELEASE "RtmClient_release"
#define FUNC_RTMCLIENT_LOGIN "RtmClient_login"
#define FUNC_RTMCLIENT_LOGOUT "RtmClient_logout"
#define FUNC_RTMCLIENT_RENEWTOKEN "RtmClient_renewToken"
#define FUNC_RTMCLIENT_PUBLISH "RtmClient_publish"
#define FUNC_RTMCLIENT_SUBSCRIBE "RtmClient_subscribe"
#define FUNC_RTMCLIENT_UNSUBSCRIBE "RtmClient_unsubscribe"
#define FUNC_RTMCLIENT_CREATESTREAMCHANNEL "RtmClient_createStreamChannel"
#define FUNC_RTMCLIENT_SETPARAMETERS "RtmClient_setParameters"
#define FUNC_RTMCLIENT_SETLOGFILE "RtmClient_setLogFile"
#define FUNC_RTMCLIENT_SETLOGLEVEL "RtmClient_setLogLevel"
#define FUNC_RTMCLIENT_SETLOGFILESIZE "RtmClient_setLogFileSize"
// class IRtmClient end

// class IRtmStorage start
#define FUNC_RTMSTORAGE_SETCHANNELMETADATA "RtmStorage_setChannelMetadata"
#define FUNC_RTMSTORAGE_UPDATECHANNELMETADATA "RtmStorage_updateChannelMetadata"
#define FUNC_RTMSTORAGE_REMOVECHANNELMETADATA "RtmStorage_removeChannelMetadata"
#define FUNC_RTMSTORAGE_GETCHANNELMETADATA "RtmStorage_getChannelMetadata"
#define FUNC_RTMSTORAGE_SETUSERMETADATA "RtmStorage_setUserMetadata"
#define FUNC_RTMSTORAGE_UPDATEUSERMETADATA "RtmStorage_updateUserMetadata"
#define FUNC_RTMSTORAGE_REMOVEUSERMETADATA "RtmStorage_removeUserMetadata"
#define FUNC_RTMSTORAGE_GETUSERMETADATA "RtmStorage_getUserMetadata"
#define FUNC_RTMSTORAGE_SUBSCRIBEUSERMETADATA "RtmStorage_subscribeUserMetadata"
#define FUNC_RTMSTORAGE_UNSUBSCRIBEUSERMETADATA                                \
  "RtmStorage_unsubscribeUserMetadata"
// class IRtmStorage end

// class IRtmLock start
#define FUNC_RTMLOCK_SETLOCK "RtmLock_setLock"
#define FUNC_RTMLOCK_GETLOCKS "RtmLock_getLocks"
#define FUNC_RTMLOCK_REMOVELOCK "RtmLock_removeLock"
#define FUNC_RTMLOCK_ACQUIRELOCK "RtmLock_acquireLock"
#define FUNC_RTMLOCK_RELEASELOCK "RtmLock_releaseLock"
#define FUNC_RTMLOCK_REVOKELOCK "RtmLock_revokeLock"
// class IRtmLock end

// class IRtmPresence start
#define FUNC_RTMPRESENCE_WHONOW "RtmPresence_whoNow"
#define FUNC_RTMPRESENCE_WHERENOW "RtmPresence_whereNow"
#define FUNC_RTMPRESENCE_SETSTATE "RtmPresence_setState"
#define FUNC_RTMPRESENCE_REMOVESTATE "RtmPresence_removeState"
#define FUNC_RTMPRESENCE_GETSTATE "RtmPresence_getState"
#define FUNC_RTMPRESENCE_GETONLINEUSERS "RtmPresence_getOnlineUsers"
#define FUNC_RTMPRESENCE_GETUSERCHANNELS "RtmPresence_getUserChannels"
// class IRtmPresence end