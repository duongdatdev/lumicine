import React, { useEffect, useState, useRef } from 'react';
import { useParams, useNavigate, useSearchParams, Link } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { discoveryApi } from '../../api/discovery';
import type {
  DiscoveryConversationDetails,
  DiscoveryConversationSummary,
  DiscoveryChatResponse,
  DiscoveryMessageView,
} from '../../api/types';
import { ApiError } from '../../api/client';
import { useAuth } from '../../auth/useAuth';
import { Card } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Badge } from '../../components/ui/Badge';
import { Alert } from '../../components/ui/Alert';
import { ErrorAlert } from '../../components/common/ErrorAlert';
import { LoadingState } from '../../components/common/LoadingState';
import { MoviePoster } from '../../components/common/PosterPlaceholder';
import { formatTimeUtc7, formatDateUtc7, formatVND, generateUUID } from '../../lib/utils';
import {
  Sparkles,
  Send,
  Plus,
  Trash2,
  Clock,
  MapPin,
  Film,
  AlertTriangle,
  RotateCcw,
  Ticket,
  Info,
} from 'lucide-react';

export const DiscoveryChatPage: React.FC = () => {
  const { id: conversationId } = useParams<{ id: string }>();
  const [searchParams] = useSearchParams();
  const initialQuery = searchParams.get('q') || '';
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { isAuthenticated } = useAuth();

  const [inputMessage, setInputMessage] = useState<string>(initialQuery);
  const [requestId, setRequestId] = useState<string>(() => generateUUID());
  const [isSending, setIsSending] = useState(false);
  const [apiError, setApiError] = useState<unknown>(null);
  const [rateLimitSeconds, setRateLimitSeconds] = useState<number | null>(null);
  const [isConversationGone, setIsConversationGone] = useState(false);

  const messagesEndRef = useRef<HTMLDivElement | null>(null);

  // Fetch list of conversations
  const { data: conversationsList, refetch: refetchConversations } = useQuery({
    queryKey: ['discoveryConversations'],
    queryFn: () => discoveryApi.listConversations({ pageSize: 50 }),
    enabled: isAuthenticated,
  });

  // Fetch active conversation
  const {
    data: activeConversation,
    isLoading: isConversationLoading,
    error: activeConversationError,
    refetch: refetchActiveConversation,
  } = useQuery({
    queryKey: ['discoveryConversation', conversationId],
    queryFn: () => discoveryApi.getConversation(conversationId!),
    enabled: isAuthenticated && !!conversationId,
  });

  // Auto-scroll to bottom
  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [activeConversation?.messages, isSending]);

  // Rate limit countdown
  useEffect(() => {
    if (rateLimitSeconds === null || rateLimitSeconds <= 0) return;
    const timer = setInterval(() => {
      setRateLimitSeconds((prev) => (prev && prev > 1 ? prev - 1 : null));
    }, 1000);
    return () => clearInterval(timer);
  }, [rateLimitSeconds]);

  // Handle creating a new conversation
  const handleCreateNewConversation = async () => {
    if (!isAuthenticated) {
      navigate('/account/login?returnTo=/discover');
      return;
    }
    setApiError(null);
    setIsConversationGone(false);
    try {
      const created = await discoveryApi.createConversation();
      await refetchConversations();
      navigate(`/discover/${created.id}`);
    } catch (err) {
      setApiError(err);
    }
  };

  // Handle deleting a conversation
  const handleDeleteConversation = async (id: string, e: React.MouseEvent) => {
    e.stopPropagation();
    if (!window.confirm('Bạn có chắc chắn muốn xóa hội thoại này?')) return;
    try {
      await discoveryApi.deleteConversation(id);
      await refetchConversations();
      if (conversationId === id) {
        navigate('/discover');
      }
    } catch (err) {
      setApiError(err);
    }
  };

  // Send discovery message
  const handleSendMessage = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!inputMessage.trim() || isSending || rateLimitSeconds) return;

    if (!isAuthenticated) {
      navigate(
        `/account/login?returnTo=${encodeURIComponent(
          conversationId ? `/discover/${conversationId}` : '/discover'
        )}`
      );
      return;
    }

    setApiError(null);
    setIsSending(true);

    let targetConvId = conversationId;

    // If no active conversation, create one first
    if (!targetConvId) {
      try {
        const created = await discoveryApi.createConversation();
        targetConvId = created.id;
        navigate(`/discover/${created.id}`, { replace: true });
      } catch (err) {
        setApiError(err);
        setIsSending(false);
        return;
      }
    }

    const expectedVersion = activeConversation?.version ?? 0;
    const payload = {
      requestId,
      expectedVersion,
      message: inputMessage.trim(),
    };

    try {
      await discoveryApi.sendMessage(targetConvId, payload);
      // Reset input & generate new requestId for next turn
      setInputMessage('');
      setRequestId(generateUUID());
      await refetchActiveConversation();
      await refetchConversations();
    } catch (err: unknown) {
      if (err instanceof ApiError) {
        if (err.status === 409) {
          // Version conflict: reload version, retain draft input
          setApiError('Hội thoại có lượt phản hồi mới. Vui lòng bấm gửi lại tin nhắn của bạn.');
          await refetchActiveConversation();
        } else if (err.status === 410) {
          // Gone: conversation expired
          setIsConversationGone(true);
        } else if (err.status === 429) {
          // Rate limit
          setRateLimitSeconds(30);
          setApiError('Bạn gửi tin nhắn quá nhanh. Vui lòng thử lại sau giây lát.');
        } else {
          setApiError(err);
        }
      } else {
        setApiError(err);
      }
    } finally {
      setIsSending(false);
    }
  };

  return (
    <div className="container mx-auto px-4 py-6 max-w-6xl">
      <div className="grid grid-cols-1 md:grid-cols-4 gap-6 h-[calc(100vh-140px)] min-h-[600px]">
        {/* Left Sidebar: Conversations History */}
        <Card className="md:col-span-1 p-4 flex flex-col h-full border-border">
          <div className="space-y-3 pb-3 border-b border-border">
            <Button
              variant="accent"
              className="w-full justify-start text-sm shadow-sm"
              onClick={handleCreateNewConversation}
            >
              <Plus className="w-4 h-4 mr-2" /> Hội thoại mới
            </Button>
          </div>

          <div className="flex-1 overflow-y-auto pt-3 space-y-1">
            <h4 className="text-xs font-bold text-foreground-muted uppercase tracking-wider px-2 mb-2">
              Lịch sử trò chuyện
            </h4>

            {conversationsList?.items && conversationsList.items.length > 0 ? (
              conversationsList.items.map((conv) => {
                const isActive = conv.id === conversationId;
                return (
                  <div
                    key={conv.id}
                    onClick={() => navigate(`/discover/${conv.id}`)}
                    className={`group flex items-center justify-between p-2.5 rounded-xl cursor-pointer text-xs font-medium transition-colors ${
                      isActive
                        ? 'bg-accent/10 text-accent font-semibold'
                        : 'text-foreground hover:bg-background-subtle'
                    }`}
                  >
                    <div className="truncate flex-1 pr-2">
                      <p className="truncate">Hội thoại #{conv.id.substring(0, 8)}</p>
                      <p className="text-[10px] text-foreground-muted mt-0.5">
                        {conv.turnCount} lượt • {formatDateUtc7(conv.lastActiveAtUtc)}
                      </p>
                    </div>

                    <button
                      onClick={(e) => handleDeleteConversation(conv.id, e)}
                      aria-label="Xóa hội thoại"
                      className="opacity-0 group-hover:opacity-100 p-1 text-foreground-muted hover:text-red-600 transition-opacity"
                    >
                      <Trash2 className="w-3.5 h-3.5" />
                    </button>
                  </div>
                );
              })
            ) : (
              <p className="text-xs text-foreground-muted px-2 py-4 italic">
                Chưa có lịch sử hội thoại nào.
              </p>
            )}
          </div>
        </Card>

        {/* Right Chat Panel */}
        <Card className="md:col-span-3 p-0 flex flex-col h-full overflow-hidden border-border bg-background-surface">
          {/* Chat Header */}
          <div className="p-4 border-b border-border bg-background-subtle/30 flex items-center justify-between">
            <div className="flex items-center gap-2">
              <div className="p-2 rounded-lg bg-accent/10 text-accent">
                <Sparkles className="w-5 h-5" />
              </div>
              <div>
                <h2 className="font-heading font-bold text-lg text-foreground">
                  Trợ lý gợi ý phim AI LumiCine
                </h2>
                <p className="text-xs text-foreground-muted">
                  Hỏi về phim, thể loại, diễn viên, cụm rạp hoặc thời gian bạn muốn xem.
                </p>
              </div>
            </div>

            {activeConversation && (
              <Button
                variant="ghost"
                size="sm"
                onClick={(e) => handleDeleteConversation(activeConversation.id, e)}
                className="text-foreground-muted hover:text-red-600"
                title="Xóa hội thoại này"
              >
                <Trash2 className="w-4 h-4" />
              </Button>
            )}
          </div>

          {/* Active Filters Display */}
          {activeConversation?.filters && (
            <div className="px-4 py-2 border-b border-border bg-background text-xs flex flex-wrap gap-1.5 items-center">
              <span className="font-semibold text-foreground-muted">Bộ lọc đang áp dụng:</span>
              {activeConversation.filters.topicText && (
                <Badge variant="outline">Chủ đề: {activeConversation.filters.topicText}</Badge>
              )}
              {activeConversation.filters.city && (
                <Badge variant="outline">Khu vực: {activeConversation.filters.city}</Badge>
              )}
              {activeConversation.filters.genreSlugs?.map((slug) => (
                <Badge key={slug} variant="neutral">
                  {slug}
                </Badge>
              ))}
              {activeConversation.filters.maxPrice && (
                <Badge variant="outline">Tối đa: {formatVND(activeConversation.filters.maxPrice)}</Badge>
              )}
            </div>
          )}

          {/* Messages Scroll Area */}
          <div className="flex-1 overflow-y-auto p-4 space-y-6">
            {isConversationLoading ? (
              <LoadingState message="Đang tải hội thoại..." />
            ) : !conversationId && (!activeConversation?.messages || activeConversation.messages.length === 0) ? (
              <div className="flex flex-col items-center justify-center text-center py-16 space-y-4 max-w-md mx-auto">
                <div className="p-4 rounded-full bg-accent/10 text-accent">
                  <Sparkles className="w-10 h-10" />
                </div>
                <h3 className="font-heading font-black text-2xl text-foreground">
                  Chào bạn! Tôi có thể giúp gì cho buổi xem phim hôm nay?
                </h3>
                <p className="text-sm text-foreground-muted leading-relaxed">
                  Bạn có thể hỏi những câu như:
                  <br />
                  <span className="italic">"Gợi ý phim hành động chiếu tối nay tại Hà Nội"</span>
                  <br />
                  <span className="italic">"Có phim hoạt hình nào phù hợp cho gia đình cuối tuần không?"</span>
                </p>
              </div>
            ) : (
              <>
                {activeConversation?.messages.map((msg, index) => (
                  <MessageBubble key={index} message={msg} />
                ))}
                {isSending && (
                  <div className="flex items-start gap-3">
                    <div className="w-8 h-8 rounded-full bg-accent text-white flex items-center justify-center shrink-0">
                      <Sparkles className="w-4 h-4 animate-spin" />
                    </div>
                    <div className="p-4 rounded-2xl bg-background-subtle border border-border text-sm text-foreground-muted space-y-1">
                      <p>AI đang tìm kiếm suất chiếu và soạn câu trả lời...</p>
                    </div>
                  </div>
                )}
                <div ref={messagesEndRef} />
              </>
            )}
          </div>

          {/* Error Notice */}
          <div className="px-4">
            <ErrorAlert error={apiError} />
            {isConversationGone && (
              <Alert variant="warning" title="Hội thoại đã hết hạn">
                Phiên trò chuyện này đã quá hạn lưu trữ.{' '}
                <button
                  onClick={handleCreateNewConversation}
                  className="font-bold underline text-primary ml-1"
                >
                  Tạo hội thoại mới ngay
                </button>
              </Alert>
            )}
          </div>

          {/* Input Area */}
          <form
            onSubmit={handleSendMessage}
            className="p-4 border-t border-border bg-background flex gap-3 items-center"
          >
            <input
              type="text"
              placeholder={
                rateLimitSeconds
                  ? `Vui lòng đợi ${rateLimitSeconds}s...`
                  : 'Nhập câu hỏi hoặc yêu cầu tìm phim...'
              }
              value={inputMessage}
              disabled={isSending || !!rateLimitSeconds || isConversationGone}
              onChange={(e) => setInputMessage(e.target.value)}
              className="flex-1 h-11 px-4 rounded-xl border border-border bg-background-surface text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-accent focus:border-transparent disabled:opacity-50"
            />
            <Button
              type="submit"
              variant="accent"
              disabled={!inputMessage.trim() || isSending || !!rateLimitSeconds || isConversationGone}
              isLoading={isSending}
              className="h-11 px-5"
            >
              <Send className="w-4 h-4 mr-1.5" /> Gửi
            </Button>
          </form>
        </Card>
      </div>
    </div>
  );
};

// Component to render individual user or assistant message
const MessageBubble: React.FC<{ message: DiscoveryMessageView }> = ({ message }) => {
  const isUser = message.role === 'user';

  if (isUser) {
    return (
      <div className="flex justify-end">
        <div className="max-w-lg rounded-2xl bg-primary text-white p-3.5 text-sm shadow-sm">
          <p className="whitespace-pre-line leading-relaxed">{message.content}</p>
        </div>
      </div>
    );
  }

  const response = message.response;

  return (
    <div className="flex items-start gap-3">
      <div className="w-8 h-8 rounded-full bg-accent text-white flex items-center justify-center shrink-0 mt-1">
        <Sparkles className="w-4 h-4" />
      </div>

      <div className="flex-1 max-w-2xl space-y-4">
        {/* Main Text Content */}
        <div className="p-4 rounded-2xl bg-background-subtle border border-border text-sm text-foreground leading-relaxed space-y-2">
          {response?.isDegraded && (
            <Badge variant="warning" className="text-[10px] mb-1">
              <AlertTriangle className="w-3 h-3 mr-1" /> Chế độ tìm kiếm cơ bản (AI tạm gián đoạn)
            </Badge>
          )}

          <p className="whitespace-pre-line">{message.content || response?.answer}</p>

          {response?.warnings && response.warnings.length > 0 && (
            <div className="pt-2 border-t border-border/60 text-xs text-amber-700 space-y-1">
              {response.warnings.map((w, idx) => (
                <p key={idx}>⚠️ {w}</p>
              ))}
            </div>
          )}
        </div>

        {/* Structured Movie & Showtime Cards */}
        {response?.movies && response.movies.length > 0 && (
          <div className="space-y-3">
            {response.movies.map((movie) => (
              <Card key={movie.movieId} className="p-4 border border-border hover:border-accent/40 transition-all space-y-3">
                <div className="flex gap-4">
                  <div className="w-20 shrink-0">
                    <MoviePoster url={movie.posterUrl} title={movie.title} aspectRatio="poster" />
                  </div>

                  <div className="flex-1 space-y-1">
                    <div className="flex items-start justify-between gap-2">
                      <Link
                        to={`/movies/${movie.movieId}`}
                        className="font-heading font-bold text-base text-foreground hover:text-accent transition-colors"
                      >
                        {movie.title}
                      </Link>
                      {movie.ageRating && (
                        <Badge variant="warning" className="text-[10px]">
                          {movie.ageRating}
                        </Badge>
                      )}
                    </div>

                    <div className="flex flex-wrap gap-1 text-[11px] text-foreground-muted">
                      <span>{movie.durationMinutes} phút</span>
                      {movie.genres?.map((g) => (
                        <span key={g} className="px-1.5 py-0.5 rounded bg-background-subtle">
                          {g}
                        </span>
                      ))}
                    </div>
                  </div>
                </div>

                {/* Showtimes for this movie */}
                {movie.showtimes && movie.showtimes.length > 0 && (
                  <div className="pt-2 border-t border-border space-y-1.5">
                    <span className="text-xs font-semibold text-foreground-muted block">
                      Các suất chiếu phù hợp:
                    </span>
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
                      {movie.showtimes.map((st) => (
                        <div
                          key={st.showtimeId}
                          className="flex items-center justify-between p-2 rounded-lg border border-border bg-background text-xs"
                        >
                          <div>
                            <div className="font-bold text-foreground">
                              {formatTimeUtc7(st.startAtUtc)} — {formatVND(st.price)}
                            </div>
                            <div className="text-[10px] text-foreground-muted truncate">
                              {st.cinemaName} • {st.availableSeats} chỗ trống
                            </div>
                          </div>

                          <Link to={`/showtimes/${st.showtimeId}/seats`}>
                            <Button variant="accent" size="sm" className="h-7 text-xs px-2.5">
                              Chọn ghế
                            </Button>
                          </Link>
                        </div>
                      ))}
                    </div>
                  </div>
                )}
              </Card>
            ))}
          </div>
        )}
      </div>
    </div>
  );
};
